using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerApiClientTests
    {
        [Test]
        public void BatchRequestUsesBoundedRouteAndParsesAcceptedAndDuplicateAcks()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue(new LedgerHttpResponse(200,
                ResponseJson(
                    AckJson("p.1", "led.one", "accepted") + "," +
                    AckJson("p.1", "led.two", "duplicate"))));
            var client = new LedgerApiClient(EnabledOptions(), transport);

            LedgerUploadResult result = client.UploadAsync(
                    new[] { Event("led.one", 1), Event("led.two", 2) },
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, result.Acknowledgements.Count);
            Assert.AreEqual(LedgerAcknowledgementStatus.Accepted,
                result.Acknowledgements[0].Status);
            Assert.AreEqual(LedgerAcknowledgementStatus.Duplicate,
                result.Acknowledgements[1].Status);
            Assert.AreEqual(1, transport.Requests.Count);
            LedgerHttpRequest request = transport.Requests[0];
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/api/v1/ledger/events", request.Url.AbsolutePath);
            Assert.AreEqual(10, request.TimeoutSeconds);
            Assert.AreEqual(262144, request.MaxResponseBytes);
            StringAssert.StartsWith(
                "{\"schemaVersion\":\"1.0.0\",\"events\":[", request.JsonBody);
            StringAssert.Contains("\"entryId\":\"led.one\"", request.JsonBody);
            StringAssert.DoesNotContain("api.example.test", request.ToString());
            StringAssert.DoesNotContain("events", request.ToString().ToLowerInvariant());
        }

        [Test]
        public void EmptyMixedPlayerAndOversizedBatchesAreRejectedBeforeTransport()
        {
            var transport = new FakeHttpTransport();
            var client = new LedgerApiClient(EnabledOptions(), transport);

            AssertFailure(LedgerTransportFailure.InvalidRequest,
                () => client.UploadAsync(Array.Empty<LedgerEvent>(), CancellationToken.None));
            AssertFailure(LedgerTransportFailure.InvalidRequest,
                () => client.UploadAsync(
                    new[] { Event("led.one", 1), Event("led.two", 2, "p.2") },
                    CancellationToken.None));
            var tooMany = new List<LedgerEvent>();
            for (int index = 0; index < 51; index++)
                tooMany.Add(Event("led." + index, index));
            AssertFailure(LedgerTransportFailure.InvalidRequest,
                () => client.UploadAsync(tooMany, CancellationToken.None));
            Assert.IsEmpty(transport.Requests);
        }

        [TestCase("accepted_unknown")]
        [TestCase("rejected")]
        public void UnknownAcknowledgementStatusIsInvalidResponse(string status)
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue(new LedgerHttpResponse(
                200, ResponseJson(AckJson("p.1", "led.one", status))));
            var client = new LedgerApiClient(EnabledOptions(), transport);

            AssertFailure(LedgerTransportFailure.InvalidResponse,
                () => client.UploadAsync(new[] { Event("led.one", 1) }, CancellationToken.None));
        }

        [Test]
        public void UnknownDuplicateOrUnsubmittedAcknowledgementsAreInvalidResponse()
        {
            AssertInvalidResponse(ResponseJson(
                AckJson("p.1", "led.other", "accepted")));
            AssertInvalidResponse(ResponseJson(
                AckJson("p.1", "led.one", "accepted") + "," +
                AckJson("p.1", "led.one", "duplicate")));
            AssertInvalidResponse(ResponseJson(
                AckJson("p.1", "led.one", "accepted").Replace(
                    "\"status\":\"accepted\"",
                    "\"status\":\"accepted\",\"serverToken\":\"no\"")));
            AssertInvalidResponse("{\"schemaVersion\":\"1.0.0\"}");
        }

        [Test]
        public void PartialAcknowledgementIsValidAndLeavesPolicyToCoordinator()
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue(new LedgerHttpResponse(
                200, ResponseJson(AckJson("p.1", "led.one", "accepted"))));
            var client = new LedgerApiClient(EnabledOptions(), transport);

            LedgerUploadResult result = client.UploadAsync(
                    new[] { Event("led.one", 1), Event("led.two", 2) },
                    CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, result.Acknowledgements.Count);
            Assert.AreEqual("led.one", result.Acknowledgements[0].Key.EntryId);
        }

        [Test]
        public void DisabledClientAndNetworkFailureMakeNoSensitiveExceptionText()
        {
            var disabledTransport = new FakeHttpTransport();
            var disabled = new LedgerApiClient(DisabledOptions(), disabledTransport);
            AssertFailure(LedgerTransportFailure.InvalidRequest,
                () => disabled.UploadAsync(new[] { Event("led.one", 1) }, CancellationToken.None));
            Assert.IsEmpty(disabledTransport.Requests);

            var failingTransport = new FakeHttpTransport
            {
                Failure = new LedgerTransportException(LedgerTransportFailure.NetworkUnavailable)
            };
            var enabled = new LedgerApiClient(EnabledOptions(), failingTransport);
            LedgerTransportException error = CaptureAsync<LedgerTransportException>(() =>
                    enabled.UploadAsync(new[] { Event("led.secret", 1) }, CancellationToken.None))
                .GetAwaiter().GetResult();
            StringAssert.DoesNotContain("http", error.Message.ToLowerInvariant());
            StringAssert.DoesNotContain("led.secret", error.Message);
        }

        [Test]
        public void UnityTransportNeverFollowsRedirects()
        {
            Assert.AreEqual(0, UnityWebRequestLedgerTransport.MaximumRedirects);
        }

        private static void AssertInvalidResponse(string response)
        {
            var transport = new FakeHttpTransport();
            transport.Responses.Enqueue(new LedgerHttpResponse(200, response));
            var client = new LedgerApiClient(EnabledOptions(), transport);
            AssertFailure(LedgerTransportFailure.InvalidResponse,
                () => client.UploadAsync(new[] { Event("led.one", 1) }, CancellationToken.None));
        }

        private static void AssertFailure(
            LedgerTransportFailure expected,
            Func<Task> action)
        {
            LedgerTransportException error = CaptureAsync<LedgerTransportException>(action)
                .GetAwaiter().GetResult();
            Assert.AreEqual(expected, error.Failure);
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

        private static LedgerRuntimeOptions EnabledOptions()
        {
            return LedgerRuntimeOptionsLoader.Parse(
                LedgerJson(true), ClientJson("https://api.example.test/"));
        }

        private static LedgerRuntimeOptions DisabledOptions()
        {
            return LedgerRuntimeOptionsLoader.Parse(
                LedgerJson(false), ClientJson(string.Empty));
        }

        private static LedgerEvent Event(string entryId, long clock, string playerId = "p.1")
        {
            return new LedgerEvent(
                entryId, playerId, LedgerEventType.EnemySpared,
                new[] { "char.hero", "npc.enemy" }, 2, "chapter.red_mist", clock,
                "rescue", new[] { "fact.enemy_alive" },
                new Dictionary<string, string>());
        }

        private static string ResponseJson(string acknowledgements)
        {
            return "{\"schemaVersion\":\"1.0.0\",\"acknowledgements\":[" +
                   acknowledgements + "]}";
        }

        private static string AckJson(string playerId, string entryId, string status)
        {
            return "{\"playerId\":\"" + playerId + "\",\"entryId\":\"" + entryId +
                   "\",\"status\":\"" + status + "\"}";
        }

        private static string LedgerJson(bool enabled)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"enabled\":" + enabled.ToString().ToLowerInvariant() + "," +
                   "\"requestTimeoutSeconds\":10," +
                   "\"maxResponseBytes\":262144," +
                   "\"batchSize\":25," +
                   "\"flushIntervalSeconds\":5," +
                   "\"maxQueuedEvents\":4096" +
                   "}";
        }

        private static string ClientJson(string baseUrl)
        {
            return "{\"schemaVersion\":\"1.0.0\",\"asrEnabled\":false," +
                   "\"apiBaseUrl\":\"" + baseUrl + "\"}";
        }

        private sealed class FakeHttpTransport : ILedgerHttpTransport
        {
            public Queue<LedgerHttpResponse> Responses { get; } =
                new Queue<LedgerHttpResponse>();
            public List<LedgerHttpRequest> Requests { get; } =
                new List<LedgerHttpRequest>();
            public LedgerTransportException Failure { get; set; }

            public Task<LedgerHttpResponse> SendAsync(
                LedgerHttpRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Add(request);
                if (Failure != null) throw Failure;
                if (Responses.Count == 0) throw new InvalidOperationException("No response queued.");
                return Task.FromResult(Responses.Dequeue());
            }
        }
    }
}
