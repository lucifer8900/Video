using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class GenerationApiClientTests
    {
        private const string Hash =
            "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string JobId = "1f6ea513-5a59-4f17-87bd-8a637dccd321";

        [Test]
        public void UnityTransportNeverFollowsRedirectsOutsideValidatedOrigins()
        {
            Assert.AreEqual(0, UnityWebRequestGenerationTransport.MaximumRedirects);
        }

        [Test]
        public void SubmitPollTicketAndDownloadUseBoundedStrictRequests()
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(202, StatusJson("queued", false, 1000, "null")));
            transport.Responses.Enqueue(new GenerationHttpResponse(200, StatusJson("ready", true, 0, "null")));
            transport.Responses.Enqueue(new GenerationHttpResponse(200, TicketJson()));
            var client = new GenerationApiClient(EnabledOptions(), transport);
            var request = new GenerationPlaybackRequest(
                "chapter.node:attempt-1",
                Hash,
                "cue.approved.fallback",
                true);

            GenerationJobSnapshot submitted = client
                .CreateOrGetAsync(request, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            GenerationJobSnapshot ready = client
                .GetAsync(JobId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            GenerationDownloadTicket ticket = client
                .CreateDownloadTicketAsync(JobId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            client.DownloadAsync(ticket, "C:/cache/generated.part", CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationJobState.Queued, submitted.State);
            Assert.AreEqual(GenerationJobState.Ready, ready.State);
            Assert.AreEqual(Hash, ticket.ContentHash);
            Assert.AreEqual(3, transport.Requests.Count);
            Assert.AreEqual("POST", transport.Requests[0].Method);
            Assert.AreEqual("/api/v1/generation/jobs", transport.Requests[0].Url.AbsolutePath);
            Assert.AreEqual(
                "{\"schemaVersion\":\"1.0.0\",\"idempotencyKey\":\"chapter.node:attempt-1\",\"inputHash\":\"" + Hash + "\"}",
                transport.Requests[0].JsonBody);
            Assert.AreEqual("GET", transport.Requests[1].Method);
            Assert.AreEqual("/api/v1/generation/jobs/" + JobId, transport.Requests[1].Url.AbsolutePath);
            Assert.AreEqual(
                "/api/v1/generation/jobs/" + JobId + "/download-ticket",
                transport.Requests[2].Url.AbsolutePath);
            Assert.AreEqual(10, transport.Requests[0].TimeoutSeconds);
            Assert.AreEqual(262144, transport.Requests[0].MaxResponseBytes);
            Assert.AreEqual(1, transport.Downloads.Count);
            Assert.AreEqual(536870912L, transport.Downloads[0].MaxBytes);
            Assert.AreEqual(ticket.Length, transport.Downloads[0].ExpectedBytes);
            Assert.AreEqual("media.example.test", transport.Downloads[0].Url.IdnHost);
            StringAssert.DoesNotContain("Signature", transport.Downloads[0].ToString());
            StringAssert.DoesNotContain("credential", transport.Downloads[0].ToString().ToLowerInvariant());
            StringAssert.DoesNotContain("Signature", ticket.ToString());
        }

        [TestCase("created", GenerationJobState.Created, false, 1000, "null")]
        [TestCase("generating", GenerationJobState.Generating, false, 1000, "null")]
        [TestCase("moderating", GenerationJobState.Moderating, false, 1000, "null")]
        [TestCase("transcoding", GenerationJobState.Transcoding, false, 1000, "null")]
        [TestCase("failed", GenerationJobState.Failed, true, 0, "\"generation.failed\"")]
        [TestCase("expired", GenerationJobState.Expired, true, 0, "\"generation.expired\"")]
        public void StatusParserAcceptsOnlyContractConsistentStates(
            string status,
            GenerationJobState expected,
            bool terminal,
            int pollMilliseconds,
            string failureJson)
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(
                200,
                StatusJson(status, terminal, pollMilliseconds, failureJson)));
            var client = new GenerationApiClient(EnabledOptions(), transport);

            GenerationJobSnapshot result = client.GetAsync(JobId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(expected, result.State);
        }

        [TestCase("{\"schemaVersion\":\"1.0.0\"}")]
        [TestCase("{\"schemaVersion\":\"1.0.0\",\"requestId\":\"req.1\",\"jobId\":\"1f6ea513-5a59-4f17-87bd-8a637dccd321\",\"status\":\"invented\",\"terminal\":false,\"pollAfterMilliseconds\":1000,\"failureCode\":null}")]
        public void MissingOrUnknownStatusDataIsInvalidResponse(string json)
        {
            GenerationTransportException error = GetInvalidStatusAsync(json)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
        }

        [Test]
        public void ExtraStatusPropertyIsRejected()
        {
            string json = StatusJson("ready", true, 0, "null").Replace(
                "\"failureCode\":null",
                "\"failureCode\":null,\"downloadUrl\":\"https://must-not-leak.test\"");

            GenerationTransportException error = GetInvalidStatusAsync(json)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
        }

        [Test]
        public void TerminalAndFailureCodeMismatchIsRejected()
        {
            GenerationTransportException error = GetInvalidStatusAsync(
                    StatusJson("ready", false, 1000, "\"generation.failed\""))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
        }

        [Test]
        public void ModerationRejectionCodeIsPreservedForCoordinatorFallbackClassification()
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(
                200,
                StatusJson("failed", true, 0, "\"moderation.rejected\"")));
            var client = new GenerationApiClient(EnabledOptions(), transport);

            GenerationJobSnapshot result = client.GetAsync(JobId, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationJobState.Failed, result.State);
            Assert.AreEqual("moderation.rejected", result.FailureCode);
        }

        [Test]
        public void UnknownFailureCodeIsRejected()
        {
            GenerationTransportException error = GetInvalidStatusAsync(
                    StatusJson("failed", true, 0, "\"provider.raw_failure\""))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
        }

        [Test]
        public void DownloadTicketFromUnlistedHostIsRejectedBeforeDownload()
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(
                200,
                TicketJson().Replace("media.example.test", "attacker.example.test")));
            var client = new GenerationApiClient(EnabledOptions(), transport);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => client.CreateDownloadTicketAsync(JobId, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
            Assert.IsEmpty(transport.Downloads);
        }

        [Test]
        public void DisabledConfigurationMakesZeroTransportCalls()
        {
            var transport = new FakeTransport();
            GenerationRuntimeOptions options = GenerationRuntimeOptionsLoader.Parse(
                "{" +
                "\"schemaVersion\":\"1.0.0\"," +
                "\"enabled\":false," +
                "\"apiBaseUrl\":\"\"," +
                "\"allowedMediaHosts\":[]," +
                "\"requestTimeoutSeconds\":10," +
                "\"maxResponseBytes\":262144," +
                "\"maxDownloadBytes\":536870912" +
                "}");
            var client = new GenerationApiClient(options, transport);
            var request = new GenerationPlaybackRequest(
                "safe-key",
                Hash,
                "cue.approved.fallback",
                true);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => client.CreateOrGetAsync(request, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.ServerRejected, error.Failure);
            Assert.IsEmpty(transport.Requests);
            Assert.IsEmpty(transport.Downloads);
        }

        [Test]
        public void UnsafeIdempotencyKeyIsRejectedBeforeTransport()
        {
            var transport = new FakeTransport();
            var client = new GenerationApiClient(EnabledOptions(), transport);
            var request = new GenerationPlaybackRequest(
                "unsafe key with spaces",
                Hash,
                "cue.approved.fallback",
                true);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => client.CreateOrGetAsync(request, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
            Assert.IsEmpty(transport.Requests);
        }

        [Test]
        public void NetworkFailureIsPreservedWithoutEmbeddingTransportDetails()
        {
            var transport = new FakeTransport
            {
                Failure = new GenerationTransportException(
                    GenerationTransportFailure.NetworkUnavailable)
            };
            var client = new GenerationApiClient(EnabledOptions(), transport);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => client.GetAsync(JobId, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.NetworkUnavailable, error.Failure);
            StringAssert.DoesNotContain("http", error.Message.ToLowerInvariant());
        }

        [Test]
        public void OversizedResponseIsRejectedEvenIfATransportViolatesItsContract()
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(
                200,
                new string('x', 262145)));
            var client = new GenerationApiClient(EnabledOptions(), transport);

            GenerationTransportException error = CaptureAsync<GenerationTransportException>(
                    () => client.GetAsync(JobId, CancellationToken.None))
                .GetAwaiter()
                .GetResult();

            Assert.AreEqual(GenerationTransportFailure.InvalidResponse, error.Failure);
        }

        private static async Task<GenerationTransportException> GetInvalidStatusAsync(string json)
        {
            var transport = new FakeTransport();
            transport.Responses.Enqueue(new GenerationHttpResponse(200, json));
            var client = new GenerationApiClient(EnabledOptions(), transport);
            return await CaptureAsync<GenerationTransportException>(
                () => client.GetAsync(JobId, CancellationToken.None));
        }

        private static GenerationRuntimeOptions EnabledOptions()
        {
            return GenerationRuntimeOptionsLoader.Parse(
                "{" +
                "\"schemaVersion\":\"1.0.0\"," +
                "\"enabled\":true," +
                "\"apiBaseUrl\":\"https://api.example.test/\"," +
                "\"allowedMediaHosts\":[\"media.example.test\"]," +
                "\"requestTimeoutSeconds\":10," +
                "\"maxResponseBytes\":262144," +
                "\"maxDownloadBytes\":536870912" +
                "}");
        }

        private static string StatusJson(
            string status,
            bool terminal,
            int pollMilliseconds,
            string failureJson)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"requestId\":\"req.1\"," +
                   "\"jobId\":\"" + JobId + "\"," +
                   "\"status\":\"" + status + "\"," +
                   "\"terminal\":" + terminal.ToString().ToLowerInvariant() + "," +
                   "\"pollAfterMilliseconds\":" + pollMilliseconds + "," +
                   "\"failureCode\":" + failureJson +
                   "}";
        }

        private static string TicketJson()
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"requestId\":\"req.2\"," +
                   "\"mediaId\":\"media.1\"," +
                   "\"contentHash\":\"" + Hash + "\"," +
                   "\"downloadUrl\":\"https://media.example.test/video.mp4?Signature=secret&credential=hidden\"," +
                   "\"expiresAtUtc\":\"2099-01-01T00:00:00Z\"," +
                   "\"contentType\":\"video/mp4\"," +
                   "\"length\":4096" +
                   "}";
        }

        private static async Task<TException> CaptureAsync<TException>(Func<Task> action)
            where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException error)
            {
                return error;
            }
            Assert.Fail("Expected exception " + typeof(TException).Name + ".");
            return null;
        }

        private sealed class FakeTransport : IGenerationHttpTransport
        {
            public Queue<GenerationHttpResponse> Responses { get; } =
                new Queue<GenerationHttpResponse>();
            public List<GenerationHttpRequest> Requests { get; } =
                new List<GenerationHttpRequest>();
            public List<GenerationDownloadRequest> Downloads { get; } =
                new List<GenerationDownloadRequest>();
            public GenerationTransportException Failure { get; set; }

            public Task<GenerationHttpResponse> SendAsync(
                GenerationHttpRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(request);
                if (Failure != null) throw Failure;
                if (Responses.Count == 0) throw new InvalidOperationException("No response queued.");
                return Task.FromResult(Responses.Dequeue());
            }

            public Task DownloadAsync(
                GenerationDownloadRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Downloads.Add(request);
                if (Failure != null) throw Failure;
                return Task.CompletedTask;
            }
        }
    }
}
