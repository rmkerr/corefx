// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Net.Http;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics;
using System.Net.Http.WinHttpHandlerUnitTests;

namespace System.Net.Http.Tests
{
    public class HttpSystemProxyTest : RemoteExecutorTestBase
    {
        private readonly ITestOutputHelper _output;
        private const string FakeProxyString = "http://proxy.contoso.com";
        private readonly Uri insecureProxyUri = new Uri("http://proxy.insecure.com");
        private readonly Uri secureProxyUri = new Uri("http://proxy.secure.com");
        private readonly Uri fooHttp = new Uri("http://foo.com");
        private readonly Uri fooHttps = new Uri("https://foo.com");
        private readonly Uri fooWs = new Uri("ws://foo.com");
        private readonly Uri fooWss = new Uri("wss://foo.com");

        public HttpSystemProxyTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData("http://proxy.insecure.com", true, false)]
        [InlineData("http=proxy.insecure.com", true, false)]
        [InlineData("http://proxy.insecure.com http://proxy.wrong.com", true, false)]
        [InlineData("https=proxy.secure.com http=proxy.insecure.com", true, true)]
        [InlineData("https://proxy.secure.com\nhttp://proxy.insecure.com", true, true)]
        [InlineData("https=proxy.secure.com\nhttp=proxy.insecure.com", true, true)]
        [InlineData("https://proxy.secure.com;http://proxy.insecure.com", true, true)]
        [InlineData("https=proxy.secure.com;http=proxy.insecure.com", true, true)]
        [InlineData(";http=proxy.insecure.com;;", true, false)]
        [InlineData("    http=proxy.insecure.com    ", true, false)]
        [InlineData("http=proxy.insecure.com;http=proxy.wrong.com", true, false)]
        public void HttpProxy_SystemProxy_Loaded(string proxyString, bool hasInsecureProxy, bool hasSecureProxy)
        {
            IWebProxy p;

            FakeRegistry.Reset();
            Assert.False(HttpSystemProxy.TryCreate(out p));

            FakeRegistry.WinInetProxySettings.Proxy = proxyString;
            WinInetProxyHelper proxyHelper = new WinInetProxyHelper();

            Assert.True(HttpSystemProxy.TryCreate(out p));
            Assert.NotNull(p);

            Assert.Equal(hasInsecureProxy ? insecureProxyUri : null, p.GetProxy(fooHttp));
            Assert.Equal(hasSecureProxy ? secureProxyUri : null, p.GetProxy(fooHttps));
            Assert.Equal(hasInsecureProxy ? insecureProxyUri : null, p.GetProxy(fooWs));
            Assert.Equal(hasSecureProxy ? secureProxyUri : null, p.GetProxy(fooWss));
        }

        [Theory]
        [InlineData("http://localhost/", true)]
        [InlineData("http://127.0.0.1/", true)]
        [InlineData("http://128.0.0.1/", false)]
        [InlineData("http://[::1]/", true)]
        [InlineData("http://foo/", true)]
        [InlineData("http://www.foo.com/", true)]
        [InlineData("http://WWW.FOO.COM/", true)]
        [InlineData("http://foo.com/", false)]
        [InlineData("http://bar.com/", true)]
        [InlineData("http://BAR.COM/", true)]
        [InlineData("http://162.1.1.1/", true)]
        [InlineData("http://[2a01:5b40:0:248::52]/", false)]
        [InlineData("http://[2002::11]/", true)]
        [InlineData("http://[2607:f8b0:4005:80a::200e]/", true)]
        [InlineData("http://[2607:f8B0:4005:80A::200E]/", true)]
        [InlineData("http://b\u00e9b\u00e9.eu/", true)]
        [InlineData("http://www.b\u00e9b\u00e9.eu/", true)]
        public void HttpProxy_Local_Bypassed(string name, bool shouldBypass)
        {
            RemoteInvoke((url, expected) =>
            {
                bool expectedResult = Boolean.Parse(expected);
                IWebProxy p;

                FakeRegistry.Reset();
                FakeRegistry.WinInetProxySettings.Proxy = FakeProxyString;
                FakeRegistry.WinInetProxySettings.ProxyBypass = "23.23.86.44;*.foo.com;<local>;BAR.COM; ; 162*;[2002::11];[*:f8b0:4005:80a::200e]; http://www.xn--mnchhausen-9db.at;http://*.xn--bb-bjab.eu;http://xn--bb-bjab.eu;";

                Assert.True(HttpSystemProxy.TryCreate(out p));
                Assert.NotNull(p);

                Uri u = new Uri(url);
                Assert.Equal(expectedResult, p.GetProxy(u) == null);

                return SuccessExitCode;
           }, name, shouldBypass.ToString()).Dispose();
        }

        [Theory]
        [InlineData("", 0)]
        [InlineData(" ", 0)]
        [InlineData(" ; ;  ", 0)]
        [InlineData("http://127.0.0.1/", 1)]
        [InlineData("[::]", 1)]
        public void HttpProxy_Local_Parsing(string bypass, int count)
        {
            RemoteInvoke((bypassValue, expected) =>
            {
                int expectedCount = Convert.ToInt32(expected);
                IWebProxy p;

                FakeRegistry.Reset();
                FakeRegistry.WinInetProxySettings.Proxy = FakeProxyString;
                FakeRegistry.WinInetProxySettings.ProxyBypass = bypassValue;

                Assert.True(HttpSystemProxy.TryCreate(out p));
                Assert.NotNull(p);

                HttpSystemProxy sp = p as HttpSystemProxy;
                Assert.NotNull(sp);

                if (expectedCount > 0)
                {
                    Assert.Equal(expectedCount, sp.BypassList.Count);
                }
                else
                {
                    Assert.Null(sp.BypassList);
                }
                return SuccessExitCode;
           }, bypass, count.ToString()).Dispose();
        }

        [InlineData("http://")]
        [InlineData("http=")]
        [InlineData("http://;")]
        [InlineData("http=;")]
        [InlineData("  ;  ")]
        [InlineData("proxy.contoso.com")]
        public void HttpProxy_InvalidSystemProxy_Null(string proxyString)
        {
            IWebProxy p;

            FakeRegistry.Reset();
            Assert.False(HttpSystemProxy.TryCreate(out p));

            FakeRegistry.WinInetProxySettings.Proxy = proxyString;
            WinInetProxyHelper proxyHelper = new WinInetProxyHelper();

            Assert.True(HttpSystemProxy.TryCreate(out p));
            Assert.NotNull(p);

            Assert.Equal(null, p.GetProxy(fooHttp));
            Assert.Equal(null, p.GetProxy(fooHttps));
        }
    }
}
