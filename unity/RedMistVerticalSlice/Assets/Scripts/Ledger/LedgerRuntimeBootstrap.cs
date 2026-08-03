using System;
using System.IO;
using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class LedgerRuntimeContext
    {
        internal LedgerRuntimeContext(
            LedgerRuntimeOptions options,
            PlayerIdentityStore identityStore,
            string playerId,
            LedgerOutbox outbox,
            LedgerUploadCoordinator uploadCoordinator,
            LedgerSettlementRecorder settlementRecorder)
        {
            Options = options;
            IdentityStore = identityStore;
            PlayerId = playerId;
            Outbox = outbox;
            UploadCoordinator = uploadCoordinator;
            SettlementRecorder = settlementRecorder;
        }

        public LedgerRuntimeOptions Options { get; }
        public PlayerIdentityStore IdentityStore { get; }
        public string PlayerId { get; }
        public LedgerOutbox Outbox { get; }
        public LedgerUploadCoordinator UploadCoordinator { get; }
        public LedgerSettlementRecorder SettlementRecorder { get; }

        public override string ToString()
        {
            return "LedgerRuntimeContext(Enabled=" + Options.Enabled + ")";
        }
    }

    public static class LedgerRuntimeBootstrap
    {
        public const string RelativeStoragePath = "lingmai-ledger-v1";
        private const string OutboxDirectoryName = "outbox";

        /// <summary>
        /// Production assembly for Unity. Component factories are lazy, so a disabled checked-in
        /// config creates neither a transport nor a retry pump.
        /// </summary>
        public static LedgerRuntimeContext Initialize(GameObject host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return Initialize(
                Application.streamingAssetsPath,
                Application.persistentDataPath,
                () => host.AddComponent<UnityWebRequestLedgerTransport>(),
                (coordinator, interval) =>
                {
                    LedgerUploadPump pump = host.AddComponent<LedgerUploadPump>();
                    pump.Initialize(coordinator, interval);
                    return pump;
                });
        }

        public static LedgerRuntimeContext Initialize(
            string streamingAssetsRoot,
            string persistentDataRoot,
            Func<ILedgerHttpTransport> transportFactory,
            Func<LedgerUploadCoordinator, int, ILedgerUploadSignal> retrySignalFactory)
        {
            if (string.IsNullOrWhiteSpace(persistentDataRoot))
                throw new ArgumentException("The persistent data root is required.", nameof(persistentDataRoot));

            // Configuration and durable identity are validated before any networking component is
            // created. A corrupt identity therefore fails closed with zero transport installation.
            LedgerRuntimeOptions options =
                LedgerRuntimeOptionsLoader.LoadFromStreamingAssets(streamingAssetsRoot);
            string storageRoot;
            try
            {
                storageRoot = Path.Combine(Path.GetFullPath(persistentDataRoot), RelativeStoragePath);
                Directory.CreateDirectory(storageRoot);
            }
            catch (Exception error) when (
                error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is NotSupportedException)
            {
                throw new LedgerOutboxException("The ledger runtime storage is unavailable.");
            }

            var identityStore = new PlayerIdentityStore(storageRoot);
            string playerId = identityStore.LoadOrCreate();
            var outbox = new LedgerOutbox(
                Path.Combine(storageRoot, OutboxDirectoryName),
                options.MaxQueuedEvents);

            LedgerUploadCoordinator coordinator = null;
            ILedgerUploadSignal signal = DisabledLedgerUploadSignal.Instance;
            if (options.Enabled)
            {
                if (transportFactory == null) throw new ArgumentNullException(nameof(transportFactory));
                if (retrySignalFactory == null) throw new ArgumentNullException(nameof(retrySignalFactory));
                ILedgerHttpTransport transport = transportFactory();
                if (transport == null)
                    throw new InvalidOperationException("The ledger transport factory returned null.");
                var client = new LedgerApiClient(options, transport);
                coordinator = new LedgerUploadCoordinator(outbox, client, options.BatchSize);
                signal = retrySignalFactory(coordinator, options.FlushIntervalSeconds);
                if (signal == null)
                    throw new InvalidOperationException("The ledger retry factory returned null.");
            }

            var recorder = new LedgerSettlementRecorder(outbox, signal, playerId);
            return new LedgerRuntimeContext(
                options,
                identityStore,
                playerId,
                outbox,
                coordinator,
                recorder);
        }

        private sealed class DisabledLedgerUploadSignal : ILedgerUploadSignal
        {
            public static readonly DisabledLedgerUploadSignal Instance =
                new DisabledLedgerUploadSignal();

            private DisabledLedgerUploadSignal()
            {
            }

            public void Signal()
            {
                // Persisting while offline is intentional; a future enabled session will retry.
            }
        }
    }
}
