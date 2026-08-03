using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerOutboxTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "red-mist-ledger-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void OfflineQueueSurvivesProcessStyleRestart()
        {
            var first = new LedgerOutbox(_root, 10);
            Assert.AreEqual(LedgerEnqueueResult.Added, first.Enqueue(Event("led.first", 2)));
            Assert.AreEqual(LedgerEnqueueResult.Added, first.Enqueue(Event("led.second", 1)));

            var restarted = new LedgerOutbox(_root, 10);
            IReadOnlyList<LedgerEvent> pending = restarted.ReadBatch(10);

            Assert.AreEqual(2, pending.Count);
            Assert.AreEqual("led.second", pending[0].EntryId);
            Assert.AreEqual("led.first", pending[1].EntryId);
            Assert.AreEqual(2, Directory.GetFiles(_root, "*.ledger.json").Length);
        }

        [Test]
        public void SamePlayerAndEntryIsStoredOnceAndConflictsFailClosed()
        {
            var outbox = new LedgerOutbox(_root, 10);
            LedgerEvent first = Event("led.same", 1);

            Assert.AreEqual(LedgerEnqueueResult.Added, outbox.Enqueue(first));
            Assert.AreEqual(LedgerEnqueueResult.AlreadyQueued, outbox.Enqueue(first));
            Assert.Throws<LedgerOutboxException>(() => outbox.Enqueue(Event(
                "led.same", 2,
                new Dictionary<string, string> { { "npcRef", "npc.changed" } })));

            Assert.AreEqual(1, Directory.GetFiles(_root, "*.ledger.json").Length);
            Assert.AreEqual(1, outbox.ReadBatch(10).Count);
        }

        [Test]
        public void AcknowledgementDeletesOnlyMatchingCommittedEntry()
        {
            var outbox = new LedgerOutbox(_root, 10);
            LedgerEvent first = Event("led.first", 1);
            LedgerEvent second = Event("led.second", 2);
            outbox.Enqueue(first);
            outbox.Enqueue(second);

            outbox.Acknowledge(new[]
            {
                new LedgerEntryKey(first.PlayerId, first.EntryId),
                new LedgerEntryKey("p.other", second.EntryId)
            });

            IReadOnlyList<LedgerEvent> remaining = outbox.ReadBatch(10);
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual(second.EntryId, remaining[0].EntryId);
        }

        [Test]
        public void CorruptCommittedFilesArePreservedAndNeverUploadedAsDefaults()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.valid", 1));
            string corruptPath = Path.Combine(_root, "corrupt.ledger.json");
            File.WriteAllText(corruptPath, "{truncated");
            string partialPath = Path.Combine(_root, "interrupted.tmp");
            File.WriteAllText(partialPath, "secret-partial-body");

            var restarted = new LedgerOutbox(_root, 10);
            IReadOnlyList<LedgerEvent> pending = restarted.ReadBatch(10);

            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("led.valid", pending[0].EntryId);
            Assert.IsTrue(restarted.HasCorruptEntries);
            Assert.IsTrue(File.Exists(corruptPath));
            Assert.IsTrue(File.Exists(partialPath));
        }

        [Test]
        public void CapacityAndReadBatchBoundariesAreEnforced()
        {
            var outbox = new LedgerOutbox(_root, 2);
            outbox.Enqueue(Event("led.one", 1));
            outbox.Enqueue(Event("led.two", 2));

            Assert.Throws<LedgerOutboxException>(() => outbox.Enqueue(Event("led.three", 3)));
            Assert.AreEqual(1, outbox.ReadBatch(1).Count);
            Assert.Throws<ArgumentOutOfRangeException>(() => outbox.ReadBatch(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LedgerOutbox(_root, 0));
        }

        [Test]
        public void OutboxStringRepresentationDoesNotRevealPathOrPayload()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event(
                "led.log", 1,
                new Dictionary<string, string> { { "npcRef", "npc.do-not-log" } }));

            string text = outbox.ToString();

            StringAssert.DoesNotContain(_root, text);
            StringAssert.DoesNotContain("do-not-log", text);
        }

        [Test]
        public void DeleteQueuedPlayerEntriesRemovesOnlyVerifiedMatchesAndPreservesCorruption()
        {
            var outbox = new LedgerOutbox(_root, 10);
            outbox.Enqueue(Event("led.p1.one", 1, null, "p.1"));
            outbox.Enqueue(Event("led.p1.two", 2, null, "p.1"));
            outbox.Enqueue(Event("led.p2", 3, null, "p.2"));
            string corrupt = Path.Combine(_root, "privacy-corrupt.ledger.json");
            File.WriteAllText(corrupt, "{not-json");

            int removed = outbox.DeleteQueuedPlayerEntries("p.1");

            Assert.AreEqual(2, removed);
            IReadOnlyList<LedgerEvent> remaining = outbox.ReadBatch(10);
            Assert.AreEqual(1, remaining.Count);
            Assert.AreEqual("p.2", remaining[0].PlayerId);
            Assert.IsTrue(File.Exists(corrupt));
        }

        private static LedgerEvent Event(
            string entryId,
            long clock,
            IReadOnlyDictionary<string, string> payload = null,
            string playerId = "p.1")
        {
            return new LedgerEvent(
                entryId, playerId, LedgerEventType.EnemySpared,
                new[] { "char.hero", "npc.enemy" }, 2, "chapter.red_mist", clock,
                "rescue", new[] { "fact.enemy_alive" },
                payload ?? new Dictionary<string, string>());
        }
    }
}
