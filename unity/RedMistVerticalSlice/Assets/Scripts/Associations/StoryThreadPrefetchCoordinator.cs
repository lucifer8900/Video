using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Immutable request context for one chapter-scoped StoryThread prefetch. The allowed slots
    /// and media identifiers are snapshotted before any asynchronous provider callback begins.
    /// </summary>
    public sealed class StoryThreadContext
    {
        private readonly IReadOnlyDictionary<string, HashSet<string>> _slotsByNode;

        public StoryThreadContext(
            string playerId,
            StoryBundleIdentity bundleIdentity,
            PlayerRoute route,
            string upcomingNodeId,
            IReadOnlyDictionary<string, StoryNode> nodes,
            IReadOnlyDictionary<string, StoryMediaAsset> mediaAssets)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                throw new ArgumentException("A player identifier is required.", nameof(playerId));
            if (bundleIdentity == null ||
                string.IsNullOrWhiteSpace(bundleIdentity.BundleId) ||
                string.IsNullOrWhiteSpace(bundleIdentity.Version) ||
                string.IsNullOrWhiteSpace(bundleIdentity.ContentHash))
            {
                throw new ArgumentException("A complete bundle identity is required.", nameof(bundleIdentity));
            }
            if (string.IsNullOrWhiteSpace(upcomingNodeId))
                throw new ArgumentException("An upcoming node is required.", nameof(upcomingNodeId));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (mediaAssets == null) throw new ArgumentNullException(nameof(mediaAssets));
            if (!nodes.ContainsKey(upcomingNodeId))
                throw new ArgumentException("The upcoming node must exist in the bundle.", nameof(upcomingNodeId));

            var nodeSnapshot = new Dictionary<string, StoryNode>(StringComparer.Ordinal);
            var slotSnapshot =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StoryNode> pair in nodes)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
                    !string.Equals(pair.Key, pair.Value.id, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The node catalog is invalid.", nameof(nodes));
                }

                var slots = new HashSet<string>(StringComparer.Ordinal);
                foreach (string point in pair.Value.injectionPoints)
                {
                    if (!IsInjectionPoint(point) || !slots.Add(point))
                        throw new ArgumentException("The node slot catalog is invalid.", nameof(nodes));
                }
                nodeSnapshot.Add(pair.Key, pair.Value);
                slotSnapshot.Add(pair.Key, slots);
            }

            var mediaSnapshot =
                new Dictionary<string, StoryMediaAsset>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StoryMediaAsset> pair in mediaAssets)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null ||
                    !string.Equals(pair.Key, pair.Value.Id, StringComparison.Ordinal))
                {
                    throw new ArgumentException("The media catalog is invalid.", nameof(mediaAssets));
                }
                mediaSnapshot.Add(pair.Key, pair.Value);
            }

            PlayerId = playerId;
            BundleIdentity = new StoryBundleIdentity(
                bundleIdentity.BundleId,
                bundleIdentity.Version,
                bundleIdentity.ContentHash);
            Route = route;
            UpcomingNodeId = upcomingNodeId;
            Nodes = new ReadOnlyDictionary<string, StoryNode>(nodeSnapshot);
            MediaAssets = new ReadOnlyDictionary<string, StoryMediaAsset>(mediaSnapshot);
            _slotsByNode =
                new ReadOnlyDictionary<string, HashSet<string>>(slotSnapshot);
        }

        public string PlayerId { get; }
        public StoryBundleIdentity BundleIdentity { get; }
        public PlayerRoute Route { get; }
        public string UpcomingNodeId { get; }
        public IReadOnlyDictionary<string, StoryNode> Nodes { get; }
        public IReadOnlyDictionary<string, StoryMediaAsset> MediaAssets { get; }

        public bool AllowsInjection(string nodeId, string point)
        {
            return !string.IsNullOrEmpty(nodeId) &&
                   !string.IsNullOrEmpty(point) &&
                   _slotsByNode.TryGetValue(nodeId, out HashSet<string> slots) &&
                   slots.Contains(point);
        }

        private static bool IsInjectionPoint(string value)
        {
            return string.Equals(value, "node_intro", StringComparison.Ordinal) ||
                   string.Equals(value, "travel_event", StringComparison.Ordinal) ||
                   string.Equals(value, "npc_mention", StringComparison.Ordinal);
        }
    }

    public interface IStoryThreadGateway
    {
        Task<StoryThread> GetNextAsync(
            StoryThreadContext context,
            CancellationToken cancellationToken);
    }

    public interface IStoryThreadDefaultCatalog
    {
        StoryThread GetDefault(StoryThreadContext context);
    }

    public interface IStoryThreadDeadline
    {
        Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
    }

    public sealed class SystemStoryThreadDeadline : IStoryThreadDeadline
    {
        public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
            return Task.Delay(timeout, cancellationToken);
        }
    }

    /// <summary>
    /// Coalesces chapter prefetches and holds only an in-memory, chapter-scoped result. Online
    /// failure is a normal path to the reviewed local default. Expiry cancels the provider and
    /// advances an epoch so a provider that ignores cancellation still cannot repopulate cache.
    /// </summary>
    public sealed class StoryThreadPrefetchCoordinator
    {
        private static readonly TimeSpan RequiredHardDeadline = TimeSpan.FromSeconds(8);

        private readonly object _gate = new object();
        private readonly IStoryThreadGateway _gateway;
        private readonly IStoryThreadDefaultCatalog _defaults;
        private readonly IStoryThreadDeadline _deadline;
        private readonly Dictionary<string, StoryThread> _cache =
            new Dictionary<string, StoryThread>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingPrefetch> _pending =
            new Dictionary<string, PendingPrefetch>(StringComparer.Ordinal);

        private CancellationTokenSource _chapterCancellation =
            new CancellationTokenSource();
        private long _epoch;
        private StoryThread _cachedThread;

        public StoryThreadPrefetchCoordinator(
            IStoryThreadGateway gateway,
            IStoryThreadDefaultCatalog defaults,
            IStoryThreadDeadline deadline)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
            _deadline = deadline ?? throw new ArgumentNullException(nameof(deadline));
        }

        public TimeSpan HardDeadline => RequiredHardDeadline;

        public StoryThread CachedThread
        {
            get
            {
                lock (_gate) return _cachedThread;
            }
        }

        public Task<StoryThread> PrefetchAsync(
            StoryThreadContext context,
            CancellationToken cancellationToken)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            cancellationToken.ThrowIfCancellationRequested();
            if (!StoryThreadContractRules.IsValid(context))
                throw new StoryThreadContractException(
                    "The StoryThread prefetch context is invalid.");
            string key = CreateKey(context);
            Task<StoryThread> shared;

            lock (_gate)
            {
                if (_cache.TryGetValue(key, out StoryThread cached))
                    return Task.FromResult(cached);
                if (_pending.TryGetValue(key, out PendingPrefetch existing))
                    shared = existing.Task;
                else
                {
                    var entry = new PendingPrefetch(
                        key,
                        _epoch,
                        _chapterCancellation.Token);
                    _pending.Add(key, entry);
                    entry.Task = RunPrefetchAsync(context, entry);
                    shared = entry.Task;
                }
            }

            return AwaitForCallerAsync(shared, cancellationToken);
        }

        public void ExpireChapter()
        {
            CancellationTokenSource expired;
            lock (_gate)
            {
                expired = _chapterCancellation;
                _chapterCancellation = new CancellationTokenSource();
                _epoch++;
                _cache.Clear();
                _pending.Clear();
                _cachedThread = null;
            }

            try
            {
                expired.Cancel();
            }
            finally
            {
                expired.Dispose();
            }
        }

        private async Task<StoryThread> RunPrefetchAsync(
            StoryThreadContext context,
            PendingPrefetch entry)
        {
            long startedTimestamp = Stopwatch.GetTimestamp();
            Task<StoryThread> onlineTask;
            try
            {
                onlineTask = _gateway.GetNextAsync(context, entry.ProviderCancellation.Token);
                if (onlineTask == null) throw new InvalidOperationException();
            }
            catch (OperationCanceledException)
            {
                if (!IsCurrent(entry))
                {
                    CompletePending(entry);
                    return null;
                }
                return CompleteWithDefault(context, entry);
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                return CompleteWithDefault(context, entry);
            }

            using (var deadlineCancellation = new CancellationTokenSource())
            {
                Task deadlineTask;
                try
                {
                    deadlineTask = _deadline.WaitAsync(
                        RequiredHardDeadline,
                        deadlineCancellation.Token);
                    if (deadlineTask == null) throw new InvalidOperationException();
                }
                catch (OperationCanceledException)
                {
                    entry.ProviderCancellation.Cancel();
                    Observe(onlineTask);
                    if (!IsCurrent(entry))
                    {
                        CompletePending(entry);
                        return null;
                    }
                    return CompleteWithDefault(context, entry);
                }
                catch (Exception error) when (!(error is OperationCanceledException))
                {
                    entry.ProviderCancellation.Cancel();
                    Observe(onlineTask);
                    return CompleteWithDefault(context, entry);
                }

                Task chapterExpired = WaitForCancellationAsync(entry.ChapterToken);
                Task completed = await Task.WhenAny(
                    onlineTask,
                    deadlineTask,
                    chapterExpired);

                if (ReferenceEquals(completed, chapterExpired))
                {
                    entry.ProviderCancellation.Cancel();
                    deadlineCancellation.Cancel();
                    Observe(onlineTask);
                    CompletePending(entry);
                    return null;
                }

                if (ReferenceEquals(completed, deadlineTask) ||
                    deadlineTask.IsCompleted ||
                    HasReachedHardDeadline(startedTimestamp))
                {
                    entry.ProviderCancellation.Cancel();
                    Observe(onlineTask);
                    return CompleteWithDefault(context, entry);
                }

                StoryThread online;
                try
                {
                    online = await onlineTask;
                }
                catch (OperationCanceledException)
                {
                    deadlineCancellation.Cancel();
                    if (!IsCurrent(entry))
                    {
                        CompletePending(entry);
                        return null;
                    }
                    return CompleteWithDefault(context, entry);
                }
                catch (Exception)
                {
                    deadlineCancellation.Cancel();
                    return CompleteWithDefault(context, entry);
                }

                if (deadlineTask.IsCompleted || HasReachedHardDeadline(startedTimestamp))
                {
                    entry.ProviderCancellation.Cancel();
                    return CompleteWithDefault(context, entry);
                }
                deadlineCancellation.Cancel();
                if (!IsValidForContext(online, context, false))
                    return CompleteWithDefault(context, entry);
                return Commit(entry, online);
            }
        }

        private StoryThread CompleteWithDefault(
            StoryThreadContext context,
            PendingPrefetch entry)
        {
            if (!IsCurrent(entry))
            {
                CompletePending(entry);
                return null;
            }

            StoryThread fallback;
            try
            {
                fallback = _defaults.GetDefault(context);
            }
            catch (Exception)
            {
                CompletePending(entry);
                return null;
            }

            if (!IsValidForContext(fallback, context, true))
            {
                CompletePending(entry);
                return null;
            }
            return Commit(entry, fallback);
        }

        private StoryThread Commit(PendingPrefetch entry, StoryThread thread)
        {
            lock (_gate)
            {
                if (entry.Epoch != _epoch || entry.ChapterToken.IsCancellationRequested)
                {
                    RemovePendingIfSame(entry);
                    return null;
                }
                _cache[entry.Key] = thread;
                _cachedThread = thread;
                RemovePendingIfSame(entry);
                return thread;
            }
        }

        private void CompletePending(PendingPrefetch entry)
        {
            lock (_gate) RemovePendingIfSame(entry);
        }

        private void RemovePendingIfSame(PendingPrefetch entry)
        {
            if (_pending.TryGetValue(entry.Key, out PendingPrefetch current) &&
                ReferenceEquals(current, entry))
            {
                _pending.Remove(entry.Key);
            }
            entry.DisposeProviderCancellation();
        }

        private bool IsCurrent(PendingPrefetch entry)
        {
            lock (_gate)
            {
                return entry.Epoch == _epoch &&
                       !entry.ChapterToken.IsCancellationRequested;
            }
        }

        private static bool IsValidForContext(
            StoryThread thread,
            StoryThreadContext context,
            bool requireFallback)
        {
            return StoryThreadValidator.TryValidate(thread, context) &&
                   (!requireFallback || thread.FallbackUsed);
        }

        private static string CreateKey(StoryThreadContext context)
        {
            return context.PlayerId + "\u001f" +
                   context.BundleIdentity.BundleId + "\u001f" +
                   context.BundleIdentity.Version + "\u001f" +
                   context.BundleIdentity.ContentHash + "\u001f" +
                   context.Route + "\u001f" +
                   context.UpcomingNodeId;
        }

        private static bool HasReachedHardDeadline(long startedTimestamp)
        {
            long elapsed = Stopwatch.GetTimestamp() - startedTimestamp;
            return elapsed < 0 ||
                   elapsed >= RequiredHardDeadline.TotalSeconds * Stopwatch.Frequency;
        }

        private static async Task<StoryThread> AwaitForCallerAsync(
            Task<StoryThread> shared,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return await shared;
            Task cancelled = WaitForCancellationAsync(cancellationToken);
            Task completed = await Task.WhenAny(shared, cancelled);
            if (ReferenceEquals(completed, shared))
            {
                return await shared;
            }
            throw new OperationCanceledException(cancellationToken);
        }

        private static Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.CompletedTask;
            return Task.Delay(Timeout.Infinite, cancellationToken);
        }

        private static void Observe(Task task)
        {
            if (task == null) return;
            task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        Exception ignored = completed.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private sealed class PendingPrefetch
        {
            private bool _disposed;

            public PendingPrefetch(
                string key,
                long epoch,
                CancellationToken chapterToken)
            {
                Key = key;
                Epoch = epoch;
                ChapterToken = chapterToken;
                ProviderCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(chapterToken);
            }

            public string Key { get; }
            public long Epoch { get; }
            public CancellationToken ChapterToken { get; }
            public CancellationTokenSource ProviderCancellation { get; }
            public Task<StoryThread> Task { get; set; }

            public void DisposeProviderCancellation()
            {
                if (_disposed) return;
                _disposed = true;
                ProviderCancellation.Dispose();
            }
        }
    }
}
