using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class AssociationRuntimeContext
    {
        private readonly string _playerId;
        private readonly StoryBundleIdentity _bundleIdentity;
        private readonly IReadOnlyDictionary<string, StoryNode> _nodes;
        private readonly IReadOnlyDictionary<string, StoryMediaAsset> _mediaAssets;
        private readonly IStoryThreadDefaultCatalog _defaults;

        internal AssociationRuntimeContext(
            AssociationRuntimeOptions options,
            StoryThreadPrefetchCoordinator coordinator,
            IStoryThreadDefaultCatalog defaults,
            bool onlineEnabled,
            StoryThreadContext initialContext)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            if (initialContext == null) throw new ArgumentNullException(nameof(initialContext));

            OnlineEnabled = onlineEnabled;
            _playerId = initialContext.PlayerId;
            _bundleIdentity = initialContext.BundleIdentity;
            _nodes = initialContext.Nodes;
            _mediaAssets = initialContext.MediaAssets;
        }

        public AssociationRuntimeOptions Options { get; }
        public StoryThreadPrefetchCoordinator Coordinator { get; }
        public bool OnlineEnabled { get; }

        public StoryThreadContext CreateStoryContext(
            PlayerRoute route,
            string upcomingNodeId)
        {
            return new StoryThreadContext(
                _playerId,
                _bundleIdentity,
                route,
                upcomingNodeId,
                _nodes,
                _mediaAssets);
        }

        public Task<StoryThread> PrefetchAsync(
            PlayerRoute route,
            string upcomingNodeId,
            CancellationToken cancellationToken)
        {
            return Coordinator.PrefetchAsync(
                CreateStoryContext(route, upcomingNodeId),
                cancellationToken);
        }

        public StoryThread GetDefault(PlayerRoute route, string upcomingNodeId)
        {
            return _defaults.GetDefault(CreateStoryContext(route, upcomingNodeId));
        }

        public void ExpireChapter()
        {
            Coordinator.ExpireChapter();
        }
    }

    /// <summary>
    /// Builds the optional association graph. The checked-in disabled configuration is evaluated
    /// before the lazy transport factory, so offline play never installs a network component.
    /// </summary>
    public static class AssociationRuntimeBootstrap
    {
        public const string DefaultThreadRelativePath =
            "Story/red-mist/association.defaults.json";

        public static AssociationRuntimeContext Initialize(
            GameObject host,
            string playerId,
            StoryBundle bundle)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (bundle == null) throw new ArgumentNullException(nameof(bundle));
            if (bundle.Identity == null ||
                string.IsNullOrWhiteSpace(bundle.EntryNodeId))
            {
                throw new ArgumentException("The story bundle is invalid.", nameof(bundle));
            }

            AssociationRuntimeOptions options =
                AssociationRuntimeOptionsLoader.LoadFromStreamingAssets(
                    Application.streamingAssetsPath);
            string defaultPath = Path.Combine(
                Application.streamingAssetsPath,
                "Story",
                "red-mist",
                "association.defaults.json");
            var defaults = new FileStoryThreadDefaultCatalog(
                defaultPath,
                StoryThreadDefaults.ApprovedRedMistDefaultsHash);

            // The runtime is assembled before route selection. The context supplies only bundle
            // catalogs here; callers create a route-bound context before each prefetch.
            PlayerRoute seedRoute = PlayerRoute.ShenYan;
            var initial = new StoryThreadContext(
                playerId,
                bundle.Identity,
                seedRoute,
                bundle.EntryNodeId,
                bundle.Nodes,
                bundle.MediaAssets);
            return Initialize(
                options,
                initial,
                defaults,
                () => host.AddComponent<UnityWebRequestAssociationTransport>());
        }

        public static AssociationRuntimeContext Initialize(
            AssociationRuntimeOptions options,
            StoryThreadContext initialContext,
            IStoryThreadDefaultCatalog defaults,
            Func<IAssociationHttpTransport> transportFactory)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (initialContext == null) throw new ArgumentNullException(nameof(initialContext));
            if (defaults == null) throw new ArgumentNullException(nameof(defaults));
            if (transportFactory == null) throw new ArgumentNullException(nameof(transportFactory));

            IStoryThreadGateway gateway = OfflineStoryThreadGateway.Instance;
            bool onlineEnabled = false;
            bool hasPrivatePlayerIdentity = !string.Equals(
                initialContext.PlayerId,
                StoryThreadDefaults.OfflinePlayerId,
                StringComparison.Ordinal);
            if (options.Enabled && hasPrivatePlayerIdentity)
            {
                IAssociationHttpTransport transport = null;
                try
                {
                    transport = transportFactory();
                }
                catch (Exception)
                {
                    // Networking is optional. Component creation fails closed to the local
                    // reviewed default without revealing platform details in a log.
                }

                if (transport != null)
                {
                    gateway = new AssociationApiClient(options, transport);
                    onlineEnabled = true;
                }
            }

            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                defaults,
                new SystemStoryThreadDeadline());
            return new AssociationRuntimeContext(
                options,
                coordinator,
                defaults,
                onlineEnabled,
                initialContext);
        }

        private sealed class OfflineStoryThreadGateway : IStoryThreadGateway
        {
            public static readonly OfflineStoryThreadGateway Instance =
                new OfflineStoryThreadGateway();

            private OfflineStoryThreadGateway()
            {
            }

            public Task<StoryThread> GetNextAsync(
                StoryThreadContext context,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromException<StoryThread>(
                    new AssociationTransportException(
                        AssociationTransportFailure.NetworkUnavailable));
            }
        }
    }
}
