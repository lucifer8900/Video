using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerUploadCoordinatorTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "red-mist-ledger-upload-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void DisconnectedFlushDefersAndRestartCanBatchRetransmit()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.one", 1));
            outbox.Enqueue(Event("led.two", 2));
            var offline = new ScriptedBatchClient
            {
                Failure = new LedgerTransportException(LedgerTransportFailure.NetworkUnavailable)
            };
            var first = new LedgerUploadCoordinator(outbox, offline, 10);

            LedgerFlushResult deferred = first.FlushOnceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(LedgerFlushStatus.Deferred, deferred.Status);
            Assert.AreEqual(LedgerTransportFailure.NetworkUnavailable, deferred.Failure);
            Assert.AreEqual(2, new LedgerOutbox(_root, 10).ReadBatch(10).Count);

            var online = new ScriptedBatchClient();
            online.Results.Enqueue(Result(
                Accepted("p.1", "led.one"),
                Accepted("p.1", "led.two")));
            var restarted = new LedgerUploadCoordinator(
                new LedgerOutbox(_root, 10), online, 10);
            LedgerFlushResult uploaded = restarted.FlushOnceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(LedgerFlushStatus.Uploaded, uploaded.Status);
            Assert.AreEqual(2, uploaded.AcknowledgedCount);
            Assert.AreEqual(0, new LedgerOutbox(_root, 10).ReadBatch(10).Count);
            Assert.AreEqual(1, online.Batches.Count);
            Assert.AreEqual(2, online.Batches[0].Count);
        }

        [Test]
        public void PartialBatchAckLeavesOneEntryForSingleEntryRetransmit()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.one", 1));
            outbox.Enqueue(Event("led.two", 2));
            outbox.Enqueue(Event("led.three", 3));
            var client = new ScriptedBatchClient();
            client.Results.Enqueue(Result(
                Accepted("p.1", "led.one"),
                Duplicate("p.1", "led.two")));
            client.Results.Enqueue(Result(Accepted("p.1", "led.three")));
            var coordinator = new LedgerUploadCoordinator(outbox, client, 3);

            LedgerFlushResult batch = coordinator.FlushOnceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            LedgerFlushResult single = coordinator.FlushOnceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(2, batch.AcknowledgedCount);
            Assert.AreEqual(1, single.AcknowledgedCount);
            Assert.AreEqual(2, client.Batches.Count);
            Assert.AreEqual(3, client.Batches[0].Count);
            Assert.AreEqual(1, client.Batches[1].Count);
            Assert.AreEqual("led.three", client.Batches[1][0].EntryId);
            Assert.IsEmpty(outbox.ReadBatch(10));
        }

        [Test]
        public void MixedPlayersAreNeverSentInSameServerBatch()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.p1.one", 1, "p.1"));
            outbox.Enqueue(Event("led.p2.one", 2, "p.2"));
            outbox.Enqueue(Event("led.p1.two", 3, "p.1"));
            var client = new ScriptedBatchClient
            {
                AcknowledgeEverything = true
            };
            var coordinator = new LedgerUploadCoordinator(outbox, client, 10);

            coordinator.FlushOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            coordinator.FlushOnceAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(2, client.Batches.Count);
            foreach (IReadOnlyList<LedgerEvent> batch in client.Batches)
            {
                string playerId = batch[0].PlayerId;
                Assert.IsTrue(AllPlayer(batch, playerId));
            }
            Assert.IsEmpty(outbox.ReadBatch(10));
        }

        [Test]
        public void CancellationAndInvalidResponseNeverDeleteQueuedEntries()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.cancel", 1));
            var cancelled = new ScriptedBatchClient { Cancel = true };
            var coordinator = new LedgerUploadCoordinator(outbox, cancelled, 10);

            Assert.Catch<OperationCanceledException>(() =>
                coordinator.FlushOnceAsync(CancellationToken.None).GetAwaiter().GetResult());
            Assert.AreEqual(1, outbox.ReadBatch(10).Count);

            var invalid = new ScriptedBatchClient
            {
                Failure = new LedgerTransportException(LedgerTransportFailure.InvalidResponse)
            };
            LedgerFlushResult deferred = new LedgerUploadCoordinator(outbox, invalid, 10)
                .FlushOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.AreEqual(LedgerFlushStatus.Deferred, deferred.Status);
            Assert.AreEqual(1, outbox.ReadBatch(10).Count);
        }

        [Test]
        public void CorruptFileDoesNotCreateDefaultEventOrBlockValidEntry()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.valid", 1));
            File.WriteAllText(Path.Combine(_root, "damaged.ledger.json"), "{}");
            var client = new ScriptedBatchClient { AcknowledgeEverything = true };
            var coordinator = new LedgerUploadCoordinator(outbox, client, 10);

            LedgerFlushResult result = coordinator.FlushOnceAsync(CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual(1, result.AcknowledgedCount);
            Assert.AreEqual(1, client.Batches.Count);
            Assert.AreEqual("led.valid", client.Batches[0][0].EntryId);
            Assert.IsTrue(outbox.HasCorruptEntries);
            Assert.IsTrue(File.Exists(Path.Combine(_root, "damaged.ledger.json")));
        }

        [Test]
        public void ConcurrentFlushesNeverUploadSameEntryTwice()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.concurrent", 1));
            var client = new BlockingBatchClient();
            var coordinator = new LedgerUploadCoordinator(outbox, client, 10);

            Task<LedgerFlushResult> first = Task.Run(
                () => coordinator.FlushOnceAsync(CancellationToken.None));
            client.Started.Task.GetAwaiter().GetResult();
            Task<LedgerFlushResult> second = Task.Run(
                () => coordinator.FlushOnceAsync(CancellationToken.None));
            client.Release.SetResult(true);
            Task.WhenAll(first, second).GetAwaiter().GetResult();

            Assert.AreEqual(1, client.CallCount);
            Assert.IsEmpty(outbox.ReadBatch(10));
        }

        [Test]
        public void EnqueueSameEventThroughCoordinatorDoesNotDuplicate()
        {
            var outbox = new LedgerOutbox(_root, 10);
            var client = new ScriptedBatchClient { AcknowledgeEverything = true };
            var coordinator = new LedgerUploadCoordinator(outbox, client, 10);
            LedgerEvent item = Event("led.once", 1);

            Assert.AreEqual(LedgerEnqueueResult.Added, coordinator.Enqueue(item));
            Assert.AreEqual(LedgerEnqueueResult.AlreadyQueued, coordinator.Enqueue(item));
            coordinator.FlushOnceAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.AreEqual(1, client.Batches.Count);
            Assert.AreEqual(1, client.Batches[0].Count);
        }

        private static bool AllPlayer(IReadOnlyList<LedgerEvent> batch, string playerId)
        {
            for (int index = 0; index < batch.Count; index++)
            {
                if (!string.Equals(batch[index].PlayerId, playerId, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static LedgerUploadResult Result(params LedgerAcknowledgement[] values)
        {
            return new LedgerUploadResult(values);
        }

        private static LedgerAcknowledgement Accepted(string playerId, string entryId)
        {
            return new LedgerAcknowledgement(
                new LedgerEntryKey(playerId, entryId),
                LedgerAcknowledgementStatus.Accepted);
        }

        private static LedgerAcknowledgement Duplicate(string playerId, string entryId)
        {
            return new LedgerAcknowledgement(
                new LedgerEntryKey(playerId, entryId),
                LedgerAcknowledgementStatus.Duplicate);
        }

        private static LedgerEvent Event(string entryId, long clock, string playerId = "p.1")
        {
            return new LedgerEvent(
                entryId, playerId, LedgerEventType.EnemySpared,
                new[] { "char.hero", "npc.enemy" }, 2, "chapter.red_mist", clock,
                "rescue", new[] { "fact.enemy_alive" },
                new Dictionary<string, string>());
        }

        private sealed class ScriptedBatchClient : ILedgerBatchClient
        {
            public Queue<LedgerUploadResult> Results { get; } =
                new Queue<LedgerUploadResult>();
            public List<IReadOnlyList<LedgerEvent>> Batches { get; } =
                new List<IReadOnlyList<LedgerEvent>>();
            public LedgerTransportException Failure { get; set; }
            public bool Cancel { get; set; }
            public bool AcknowledgeEverything { get; set; }

            public Task<LedgerUploadResult> UploadAsync(
                IReadOnlyList<LedgerEvent> events,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Batches.Add(new List<LedgerEvent>(events).AsReadOnly());
                if (Cancel) return Cancelled<LedgerUploadResult>();
                if (Failure != null) throw Failure;
                if (AcknowledgeEverything)
                {
                    var acknowledgements = new List<LedgerAcknowledgement>();
                    for (int index = 0; index < events.Count; index++)
                    {
                        acknowledgements.Add(Accepted(
                            events[index].PlayerId, events[index].EntryId));
                    }
                    return Task.FromResult(new LedgerUploadResult(acknowledgements));
                }
                return Task.FromResult(Results.Dequeue());
            }
        }

        private sealed class BlockingBatchClient : ILedgerBatchClient
        {
            public TaskCompletionSource<bool> Started { get; } =
                new TaskCompletionSource<bool>();
            public TaskCompletionSource<bool> Release { get; } =
                new TaskCompletionSource<bool>();
            public int CallCount { get; private set; }

            public async Task<LedgerUploadResult> UploadAsync(
                IReadOnlyList<LedgerEvent> events,
                CancellationToken cancellationToken)
            {
                CallCount++;
                Started.TrySetResult(true);
                await Release.Task;
                cancellationToken.ThrowIfCancellationRequested();
                return Result(Accepted(events[0].PlayerId, events[0].EntryId));
            }
        }

        private static Task<T> Cancelled<T>()
        {
            var completion = new TaskCompletionSource<T>();
            completion.SetCanceled();
            return completion.Task;
        }
    }
}
