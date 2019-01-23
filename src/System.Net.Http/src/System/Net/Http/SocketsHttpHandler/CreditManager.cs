﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace System.Net.Http
{
    internal class CreditManager : IDisposable
    {
        private struct Waiter
        {
            public int Amount;
            public TaskCompletionSource<int> TaskCompletionSource;
            public CancellationTokenRegistration TokenRegistration;
        }

        private int _current;
        private object _syncObject;
        private Queue<Waiter> _waiters;
        private bool _disposed;

        public CreditManager(int initialCredit)
        {
            _current = initialCredit;
            _syncObject = new object();
            _waiters = null;
            _disposed = false;
        }

        public ValueTask<int> RequestCreditAsync(int amount, CancellationToken cancellationToken)
        {
            lock (_syncObject)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(CreditManager));
                }

                if (_current > 0)
                {
                    Debug.Assert(_waiters == null || _waiters.Count == 0, "Shouldn't have waiters when credit is available");

                    int granted = Math.Min(amount, _current);
                    _current -= granted;
                    return new ValueTask<int>(granted);
                }

                var tcs = new TaskCompletionSource<int>(TaskContinuationOptions.RunContinuationsAsynchronously);

                if (_waiters == null)
                {
                    _waiters = new Queue<Waiter>();
                }

                Waiter waiter = new Waiter { Amount = amount,
                                             TaskCompletionSource = tcs,
                                             TokenRegistration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)) };

                _waiters.Enqueue(waiter);

                return new ValueTask<int>(tcs.Task);
            }
        }

        public void AdjustCredit(int amount)
        {
            // Note credit can be adjusted *downward* as well.
            // This can cause the current credit to become negative.

            lock (_syncObject)
            {
                if (_disposed)
                {
                    return;
                }

                Debug.Assert(_current <= 0 || _waiters == null || _waiters.Count == 0, "Shouldn't have waiters when credit is available");

                checked
                {
                    _current += amount;
                }

                if (_waiters != null)
                {
                    while (_current > 0 && _waiters.TryDequeue(out Waiter waiter))
                    {
                        waiter.TokenRegistration.Dispose();

                        // If we have the code below (TrySetResult), do we even actually need this?
                        // Might be worth optimizing for case where operation is not cancelled.
                        if (waiter.TaskCompletionSource.Task.IsCanceled)
                        {
                            continue;
                        }

                        int granted = Math.Min(waiter.Amount, _current);

                        // TODO: Determine what happens if a cancellation callback is in-progress when the
                        // TokenRegistration is disposed. It's possible I don't need this code (but I doubt it).
                        // Handle a race between the the Dispose() on TokenRegistration and a call to the cancellation callback.
                        if (waiter.TaskCompletionSource.TrySetResult(granted))
                        {
                            _current -= granted;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_syncObject)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (_waiters != null)
                {
                    while (_waiters.TryDequeue(out Waiter waiter))
                    {
                        waiter.TokenRegistration.Dispose();
                        waiter.TaskCompletionSource.TrySetException(new ObjectDisposedException(nameof(CreditManager)));
                    }
                }
            }
        }
    }
}
