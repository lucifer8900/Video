using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerSettlementRecorderTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "red-mist-ledger-recorder-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ReviewedSyntheticNodeSettlementPersistsOnceThenSignalsUpload()
        {
            var outbox = new LedgerOutbox(_root, 10);
            var signal = new RecordingUploadSignal();
            var recorder = new LedgerSettlementRecorder(outbox, signal);
            LedgerEvent reviewed = ReviewedSyntheticEvent();

            Assert.AreEqual(LedgerEnqueueResult.Added, recorder.Record(reviewed));
            Assert.AreEqual(LedgerEnqueueResult.AlreadyQueued, recorder.Record(reviewed));

            Assert.AreEqual(1, signal.Count);
            IReadOnlyList<LedgerEvent> pending = new LedgerOutbox(_root, 10).ReadBatch(10);
            Assert.AreEqual(1, pending.Count);
            Assert.AreEqual("node.synthetic_settlement", pending[0].SourceNodeId);
            Assert.AreEqual("p.opaque_8842", pending[0].PlayerId);
            Assert.AreEqual("led.synthetic_once", pending[0].EntryId);
        }

        [Test]
        public void RecorderAcceptsOnlyACompleteReviewedEventAndDoesNotMapActionText()
        {
            var recorder = new LedgerSettlementRecorder(
                new LedgerOutbox(_root, 10), new RecordingUploadSignal());

            Assert.Throws<ArgumentNullException>(() => recorder.Record(null));
            Assert.IsNull(typeof(LedgerSettlementRecorder).GetMethod("RecordActionLabel"));
            Assert.IsNull(typeof(LedgerSettlementRecorder).GetMethod("InferFromText"));
        }

        private static LedgerEvent ReviewedSyntheticEvent()
        {
            return new LedgerEvent(
                "led.synthetic_once",
                "p.opaque_8842",
                LedgerEventType.PromiseMade,
                new[] { "char.synthetic" },
                2,
                "chapter.synthetic",
                45,
                "node.synthetic_settlement",
                new[] { "fact.synthetic_reviewed" },
                new Dictionary<string, string> { { "promiseRef", "promise.synthetic_test" } });
        }

        private sealed class RecordingUploadSignal : ILedgerUploadSignal
        {
            public int Count { get; private set; }
            public void Signal() => Count++;
        }
    }
}
