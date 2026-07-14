using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MosquitoNetCalculator.Tests.Helpers
{
    /// <summary>
    /// A mock <see cref="HttpMessageHandler"/> for unit tests that need to
    /// inject fake HTTP responses without making real network calls.
    /// </summary>
    public sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
