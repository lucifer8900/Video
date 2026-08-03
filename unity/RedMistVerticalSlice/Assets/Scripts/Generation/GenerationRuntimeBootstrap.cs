using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Owns the production generation graph without binding it to a story node. The checked-in
    /// configuration is offline, and callers cannot provide their own online-enabled flag.
    /// </summary>
    public sealed class GenerationRuntimeBootstrap : MonoBehaviour
    {
        private GenerationPlaybackCoordinator _coordinator;
        private bool _onlineEnabled;

        public bool IsInitialized { get; private set; }
        public bool ConfigurationValid { get; private set; }
        public bool OnlineEnabled => IsInitialized && _onlineEnabled;
        public bool TransportInstalled { get; private set; }

        /// <summary>
        /// Production initialization. It adds UnityWebRequest transport only when the strict
        /// checked-in configuration explicitly enables the online enhancement.
        /// </summary>
        public void Initialize(IMediaDirectorPlaybackPort mediaDirector)
        {
            if (mediaDirector == null) throw new ArgumentNullException(nameof(mediaDirector));
            string cacheRoot = Path.Combine(
                Application.persistentDataPath,
                "generated-media-cache");
            Initialize(
                mediaDirector,
                Application.streamingAssetsPath,
                cacheRoot,
                () => gameObject.AddComponent<UnityWebRequestGenerationTransport>());
        }

        /// <summary>
        /// Injectable assembly seam for EditMode tests. The transport factory is deliberately
        /// lazy: disabled or rejected configuration must never invoke it.
        /// </summary>
        public void Initialize(
            IMediaDirectorPlaybackPort mediaDirector,
            string streamingAssetsRoot,
            string cacheRoot,
            Func<IGenerationHttpTransport> transportFactory)
        {
            if (IsInitialized) throw new InvalidOperationException(
                "The generation runtime is already initialized.");
            if (mediaDirector == null) throw new ArgumentNullException(nameof(mediaDirector));
            if (string.IsNullOrWhiteSpace(streamingAssetsRoot))
                throw new ArgumentException(
                    "A StreamingAssets root is required.",
                    nameof(streamingAssetsRoot));
            if (string.IsNullOrWhiteSpace(cacheRoot) || !Path.IsPathRooted(cacheRoot))
                throw new ArgumentException(
                    "An absolute generated-media cache root is required.",
                    nameof(cacheRoot));
            if (transportFactory == null) throw new ArgumentNullException(nameof(transportFactory));

            string verifiedCacheRoot = PrepareCacheRoot(cacheRoot);
            var playback = new MediaDirectorPlaybackGateway(mediaDirector, verifiedCacheRoot);
            var clock = new UnityGenerationFlowClock();
            GenerationRuntimeOptions options = TryLoadOptions(streamingAssetsRoot);

            IGenerationJobGateway jobs;
            IVerifiedMediaCache cache;
            if (options != null && options.Enabled)
            {
                IGenerationHttpTransport transport = null;
                try
                {
                    transport = transportFactory();
                }
                catch (Exception)
                {
                    // Component creation or platform networking may be unavailable. This is an
                    // optional enhancement, so construction fails closed to local fallback.
                }

                if (transport != null)
                {
                    var client = new GenerationApiClient(options, transport);
                    jobs = client;
                    cache = new VerifiedMediaCache(
                        verifiedCacheRoot,
                        options.AllowedMediaHosts,
                        options.MaxDownloadBytes,
                        client,
                        () => clock.UtcNow);
                    TransportInstalled = true;
                    _onlineEnabled = true;
                }
                else
                {
                    var offline = new OfflineGenerationBackend();
                    jobs = offline;
                    cache = offline;
                }
            }
            else
            {
                var offline = new OfflineGenerationBackend();
                jobs = offline;
                cache = offline;
            }

            _coordinator = new GenerationPlaybackCoordinator(jobs, cache, playback, clock);
            IsInitialized = true;
        }

        /// <summary>
        /// Creates the only request form accepted by this runtime. OnlineGenerationEnabled is
        /// always derived from the immutable initialization result, never from caller input.
        /// </summary>
        public GenerationPlaybackRequest CreateRequest(
            string idempotencyKey,
            string inputHash,
            string fallbackCueId,
            double totalTimeoutSeconds = 30d,
            double pollIntervalSeconds = 1d)
        {
            EnsureInitialized();
            return new GenerationPlaybackRequest(
                idempotencyKey,
                inputHash,
                fallbackCueId,
                _onlineEnabled,
                totalTimeoutSeconds,
                pollIntervalSeconds);
        }

        public Task<GenerationPlaybackResult> RunAsync(
            string idempotencyKey,
            string inputHash,
            string fallbackCueId,
            double totalTimeoutSeconds = 30d,
            double pollIntervalSeconds = 1d)
        {
            EnsureInitialized();
            return _coordinator.RunAsync(CreateRequest(
                idempotencyKey,
                inputHash,
                fallbackCueId,
                totalTimeoutSeconds,
                pollIntervalSeconds));
        }

        private GenerationRuntimeOptions TryLoadOptions(string streamingAssetsRoot)
        {
            try
            {
                GenerationRuntimeOptions options =
                    GenerationRuntimeOptionsLoader.LoadFromStreamingAssets(streamingAssetsRoot);
                ConfigurationValid = true;
                return options;
            }
            catch (GenerationRuntimeConfigurationException)
            {
                ConfigurationValid = false;
                return null;
            }
        }

        private static string PrepareCacheRoot(string cacheRoot)
        {
            try
            {
                string fullPath = Path.GetFullPath(cacheRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(fullPath);
                return fullPath;
            }
            catch (Exception error) when (
                error is ArgumentException ||
                error is NotSupportedException ||
                error is PathTooLongException ||
                error is IOException ||
                error is UnauthorizedAccessException)
            {
                throw new ArgumentException(
                    "The generated-media cache root could not be prepared.",
                    nameof(cacheRoot));
            }
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized || _coordinator == null)
                throw new InvalidOperationException(
                    "The generation runtime is not initialized.");
        }

        private sealed class OfflineGenerationBackend :
            IGenerationJobGateway,
            IVerifiedMediaCache
        {
            public Task<GenerationJobSnapshot> CreateOrGetAsync(
                GenerationPlaybackRequest request,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<GenerationJobSnapshot>(OfflineFailure());
            }

            public Task<GenerationJobSnapshot> GetAsync(
                string jobId,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<GenerationJobSnapshot>(OfflineFailure());
            }

            public Task<GenerationDownloadTicket> CreateDownloadTicketAsync(
                string jobId,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<GenerationDownloadTicket>(OfflineFailure());
            }

            public Task<VerifiedMediaHandle> GetOrDownloadAsync(
                GenerationDownloadTicket ticket,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<VerifiedMediaHandle>(OfflineFailure());
            }

            private static GenerationTransportException OfflineFailure()
            {
                return new GenerationTransportException(
                    GenerationTransportFailure.ServerRejected);
            }
        }
    }
}
