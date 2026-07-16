using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerRuntimeBootstrapTests
    {
        private string _streaming;
        private string _persistent;

        [SetUp]
        public void SetUp()
        {
            string root = Path.Combine(Path.GetTempPath(), "red-mist-ledger-bootstrap-tests", Guid.NewGuid().ToString("N"));
            _streaming = Path.Combine(root, "StreamingAssets");
            _persistent = Path.Combine(root, "PersistentData");
            Directory.CreateDirectory(Path.Combine(_streaming, "Config"));
            Directory.CreateDirectory(_persistent);
        }

        [TearDown]
        public void TearDown()
        {
            string root = Directory.GetParent(_streaming).FullName;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void OfflineBootstrapStillOpensIdentityOutboxAndReviewedRecorder()
        {
            WriteConfiguration(false, string.Empty);
            int transports = 0;
            int retrySignals = 0;

            LedgerRuntimeContext context = LedgerRuntimeBootstrap.Initialize(
                _streaming,
                _persistent,
                () =>
                {
                    transports++;
                    return new FakeHttpTransport();
                },
                (_, __) =>
                {
                    retrySignals++;
                    return new RecordingSignal();
                });

            Assert.IsFalse(context.Options.Enabled);
            Assert.IsNotNull(context.Outbox);
            Assert.IsNotNull(context.IdentityStore);
            Assert.IsNotNull(context.PlayerId);
            Assert.IsNotNull(context.SettlementRecorder);
            Assert.IsNull(context.UploadCoordinator);
            Assert.AreEqual(0, transports);
            Assert.AreEqual(0, retrySignals);

            LedgerEvent reviewed = Event(context.PlayerId, "led.offline");
            Assert.AreEqual(LedgerEnqueueResult.Added,
                context.SettlementRecorder.Record(reviewed));
            Assert.AreEqual(1, context.Outbox.ReadBatch(10).Count);
        }

        [Test]
        public void EnabledBootstrapInstallsTransportAndRetryWithoutSendingNetworkRequest()
        {
            WriteConfiguration(true, "https://api.example.test/");
            var transport = new FakeHttpTransport();
            var signal = new RecordingSignal();
            int transports = 0;
            int retryPumps = 0;

            LedgerRuntimeContext context = LedgerRuntimeBootstrap.Initialize(
                _streaming,
                _persistent,
                () =>
                {
                    transports++;
                    return transport;
                },
                (coordinator, interval) =>
                {
                    Assert.IsNotNull(coordinator);
                    Assert.AreEqual(5, interval);
                    retryPumps++;
                    return signal;
                });

            Assert.IsTrue(context.Options.Enabled);
            Assert.IsNotNull(context.UploadCoordinator);
            Assert.AreEqual(1, transports);
            Assert.AreEqual(1, retryPumps);
            Assert.AreEqual(0, transport.SendCount);

            context.SettlementRecorder.Record(Event(context.PlayerId, "led.enabled"));
            Assert.AreEqual(1, signal.Count);
            Assert.AreEqual(0, transport.SendCount);
        }

        [Test]
        public void CorruptIdentityPreventsTransportInstallationAndIsPreserved()
        {
            WriteConfiguration(true, "https://api.example.test/");
            string identityRoot = Path.Combine(_persistent, LedgerRuntimeBootstrap.RelativeStoragePath);
            Directory.CreateDirectory(identityRoot);
            string identityPath = Path.Combine(identityRoot, PlayerIdentityStore.FileName);
            File.WriteAllText(identityPath, "{corrupt");
            int transports = 0;
            int pumps = 0;

            Assert.Throws<PlayerIdentityException>(() => LedgerRuntimeBootstrap.Initialize(
                _streaming,
                _persistent,
                () =>
                {
                    transports++;
                    return new FakeHttpTransport();
                },
                (_, __) =>
                {
                    pumps++;
                    return new RecordingSignal();
                }));

            Assert.AreEqual(0, transports);
            Assert.AreEqual(0, pumps);
            Assert.AreEqual("{corrupt", File.ReadAllText(identityPath));
        }

        [Test]
        public void ProductionRecorderRejectsAnEventForAnotherPlayer()
        {
            WriteConfiguration(false, string.Empty);
            LedgerRuntimeContext context = LedgerRuntimeBootstrap.Initialize(
                _streaming,
                _persistent,
                () => new FakeHttpTransport(),
                (_, __) => new RecordingSignal());

            Assert.Throws<ArgumentException>(() =>
                context.SettlementRecorder.Record(Event("p.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "led.wrong_player")));
            Assert.IsEmpty(context.Outbox.ReadBatch(10));
        }

        [Test]
        public void InvalidConfigurationNeverInstallsTransportOrRetryPump()
        {
            WriteConfiguration(true, "https://api.example.test/");
            string ledgerPath = Path.Combine(
                _streaming, LedgerRuntimeOptionsLoader.LedgerRelativePath);
            File.AppendAllText(ledgerPath, "{trailing");
            int transports = 0;
            int pumps = 0;

            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeBootstrap.Initialize(
                    _streaming,
                    _persistent,
                    () =>
                    {
                        transports++;
                        return new FakeHttpTransport();
                    },
                    (_, __) =>
                    {
                        pumps++;
                        return new RecordingSignal();
                    }));

            Assert.AreEqual(0, transports);
            Assert.AreEqual(0, pumps);
        }

        private void WriteConfiguration(bool enabled, string apiBaseUrl)
        {
            File.WriteAllText(
                Path.Combine(_streaming, LedgerRuntimeOptionsLoader.LedgerRelativePath),
                "{" +
                "\"schemaVersion\":\"1.0.0\"," +
                "\"enabled\":" + enabled.ToString().ToLowerInvariant() + "," +
                "\"requestTimeoutSeconds\":10," +
                "\"maxResponseBytes\":262144," +
                "\"batchSize\":25," +
                "\"flushIntervalSeconds\":5," +
                "\"maxQueuedEvents\":4096" +
                "}");
            File.WriteAllText(
                Path.Combine(_streaming, LedgerRuntimeOptionsLoader.ClientRelativePath),
                "{\"schemaVersion\":\"1.0.0\",\"asrEnabled\":false," +
                "\"apiBaseUrl\":\"" + apiBaseUrl + "\"}");
        }

        private static LedgerEvent Event(string playerId, string entryId)
        {
            return new LedgerEvent(
                entryId,
                playerId,
                LedgerEventType.ItemGained,
                new[] { "char.synthetic" },
                1,
                "chapter.synthetic",
                1,
                "synthetic_settlement",
                Array.Empty<string>(),
                new Dictionary<string, string>());
        }

        private sealed class RecordingSignal : ILedgerUploadSignal
        {
            public int Count { get; private set; }
            public void Signal() => Count++;
        }

        private sealed class FakeHttpTransport : ILedgerHttpTransport
        {
            public int SendCount { get; private set; }

            public Task<LedgerHttpResponse> SendAsync(
                LedgerHttpRequest request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                throw new AssertionException("Bootstrap must not send a network request.");
            }
        }
    }
}
