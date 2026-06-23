using System;
using System.Net.Http;
using clicky_windows;
using Xunit;

namespace clicky_windows_tests
{
    /// <summary>
    /// Tests for AssemblyAIClient.ResolveTokenRequest — the pure logic that decides
    /// whether to fetch a streaming token from a Clicky Cloudflare Worker (POST
    /// {worker}/transcribe-token, no key) or directly from AssemblyAI
    /// (GET streaming.assemblyai.com/v3/token with the user's key). This was the
    /// root cause of SPEC-01: the old code always used the worker route, which 404'd
    /// against the shipped default endpoint.
    /// </summary>
    public class AssemblyAiTokenResolutionTests
    {
        [Fact]
        public void BlankEndpoint_WithKey_ResolvesToDirectTokenEndpointWithKey()
        {
            var request = AssemblyAIClient.ResolveTokenRequest(
                configuredEndpoint: "",
                assemblyAiApiKey: "aai-test-key");

            Assert.Equal("https://streaming.assemblyai.com/v3/token", request.Url);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("aai-test-key", request.AuthorizationHeader);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BlankEndpoint_WithNoKey_ThrowsClearConfigurationError(string? endpoint)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                AssemblyAIClient.ResolveTokenRequest(endpoint, assemblyAiApiKey: ""));

            // The message must guide the user to fix it in Settings, not reference
            // internal symbols — this is what surfaces in the error UI/log.
            Assert.Contains("Settings", ex.Message);
            Assert.Contains("AssemblyAI", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void WorkerUrl_ResolvesToWorkerTranscribeTokenRoute_WithNoKey()
        {
            var request = AssemblyAIClient.ResolveTokenRequest(
                configuredEndpoint: "https://my-clicky.example.workers.dev",
                assemblyAiApiKey: "");

            Assert.Equal("https://my-clicky.example.workers.dev/transcribe-token", request.Url);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.AuthorizationHeader);
        }

        [Fact]
        public void WorkerUrl_WithTrailingSlash_IsTrimmedBeforeAppendingRoute()
        {
            var request = AssemblyAIClient.ResolveTokenRequest(
                configuredEndpoint: "https://my-clicky.example.workers.dev/",
                assemblyAiApiKey: null);

            // No double slash — the route must be a clean single path segment.
            Assert.Equal("https://my-clicky.example.workers.dev/transcribe-token", request.Url);
        }

        [Fact]
        public void WorkerUrl_KeyIsIgnored_InWorkerMode()
        {
            // In worker mode the key lives on the server; even if a key was typed,
            // it must never be sent from the client to the worker (leaking nothing).
            var request = AssemblyAIClient.ResolveTokenRequest(
                configuredEndpoint: "https://proxy.example.com",
                assemblyAiApiKey: "should-not-be-sent");

            Assert.Null(request.AuthorizationHeader);
        }

        [Theory]
        [InlineData("https://api.assemblyai.com")]
        [InlineData("https://streaming.assemblyai.com/v3/token")]
        [InlineData("https://api.assemblyai.com/v2")] // the old broken default
        public void AssemblyAiHost_Endpoint_ResolvesToDirectModeNotWorkerRoute(string endpoint)
        {
            // Even if someone pastes an AssemblyAI host into the endpoint, it must
            // not be treated as a worker (which would hit /transcribe-token and 404).
            var request = AssemblyAIClient.ResolveTokenRequest(endpoint, "real-key");

            Assert.Equal("https://streaming.assemblyai.com/v3/token", request.Url);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("real-key", request.AuthorizationHeader);
        }

        [Theory]
        [InlineData("not-a-url")]
        [InlineData("ftp://example.com")]
        [InlineData("your-worker-name.your-subdomain.workers.dev")]
        public void NonHttpAbsoluteUrl_FallsBackToDirectModeWithKey(string endpoint)
        {
            var request = AssemblyAIClient.ResolveTokenRequest(endpoint, "real-key");

            Assert.Equal("https://streaming.assemblyai.com/v3/token", request.Url);
            Assert.Equal("real-key", request.AuthorizationHeader);
        }

        [Fact]
        public void PlaceholderWorkerDomain_WithoutKey_ThrowsConfigurationError()
        {
            // The legacy "your-worker-name" placeholder is a relative-looking string
            // (no scheme), so it must NOT be treated as a worker URL — it should
            // fall through to direct mode and, lacking a key, throw a clear error
            // instead of ever contacting the placeholder host.
            Assert.Throws<InvalidOperationException>(() =>
                AssemblyAIClient.ResolveTokenRequest(
                    "your-worker-name.your-subdomain.workers.dev",
                    assemblyAiApiKey: ""));
        }
    }
}
