using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Lingmai.RedMist.Tests
{
    public sealed class StoryThreadPrefetchCoordinatorTests
    {
        [Test]
        public void EndingInvalidatesCompletedAndLatePresentationTokens()
        {
            var lifetime = new StoryThreadChapterLifetime();
            long completedToken = lifetime.Begin();
            long lateToken = completedToken;

            lifetime.End();

            Assert.IsFalse(lifetime.IsCurrent(completedToken));
            Assert.IsFalse(lifetime.IsCurrent(lateToken));
            long nextChapterToken = lifetime.Begin();
            Assert.IsTrue(lifetime.IsCurrent(nextChapterToken));
            Assert.AreNotEqual(completedToken, nextChapterToken);
        }

        [UnityTest]
        public IEnumerator OfflineFailureUsesValidatedDefaultThread()
        {
            StoryThreadContext context = Context();
            StoryThread fallback = Thread(context.PlayerId, true);
            var gateway = new FailingGateway();
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(fallback),
                new NeverDeadline());

            Task<StoryThread> pending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!pending.IsCompleted) yield return null;
            StoryThread result = pending.GetAwaiter().GetResult();

            Assert.AreSame(fallback, result);
            Assert.AreEqual(1, gateway.CallCount);
            Assert.IsTrue(result.FallbackUsed);
        }

        [UnityTest]
        public IEnumerator HardDeadlineFallsBackAndCancelsOnlineRequest()
        {
            StoryThreadContext context = Context();
            var gateway = new BlockingGateway();
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(Thread(context.PlayerId, true)),
                new ImmediateDeadline());

            Task<StoryThread> pending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!pending.IsCompleted) yield return null;
            StoryThread result = pending.GetAwaiter().GetResult();

            Assert.IsTrue(result.FallbackUsed);
            Assert.IsTrue(gateway.CancellationObserved);
            Assert.AreEqual(TimeSpan.FromSeconds(8), coordinator.HardDeadline);
        }

        [UnityTest]
        public IEnumerator SimultaneousOnlineAndDeadlineCompletionAlwaysFallsBack()
        {
            StoryThreadContext context = Context();
            StoryThread fallback = Thread(context.PlayerId, true);
            var coordinator = new StoryThreadPrefetchCoordinator(
                new ImmediateGateway(Thread(context.PlayerId, false)),
                new FixedDefaults(fallback),
                new ImmediateDeadline());

            Task<StoryThread> pending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!pending.IsCompleted) yield return null;

            Assert.AreSame(fallback, pending.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator ChapterExpiryDropsLateResponseAndClearsCache()
        {
            StoryThreadContext context = Context();
            var gateway = new ManuallyCompletedGateway();
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(Thread(context.PlayerId, true)),
                new NeverDeadline());

            Task<StoryThread> pending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            coordinator.ExpireChapter();
            gateway.Complete(Thread(context.PlayerId, false));

            while (!pending.IsCompleted) yield return null;
            Assert.IsNull(pending.GetAwaiter().GetResult());
            Assert.IsNull(coordinator.CachedThread);
        }

        [UnityTest]
        public IEnumerator SameChapterRequestIsCoalescedAndCached()
        {
            StoryThreadContext context = Context();
            StoryThread online = Thread(context.PlayerId, false);
            var gateway = new ImmediateGateway(online);
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(Thread(context.PlayerId, true)),
                new NeverDeadline());

            Task<StoryThread> firstPending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!firstPending.IsCompleted) yield return null;
            StoryThread first = firstPending.GetAwaiter().GetResult();
            Task<StoryThread> secondPending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!secondPending.IsCompleted) yield return null;
            StoryThread second = secondPending.GetAwaiter().GetResult();

            Assert.AreSame(online, first);
            Assert.AreSame(first, second);
            Assert.AreEqual(1, gateway.CallCount);
        }

        [UnityTest]
        public IEnumerator InvalidOnlineThreadFallsBackWithoutCachingTheInvalidPlayerThread()
        {
            StoryThreadContext context = Context();
            StoryThread fallback = Thread(context.PlayerId, true);
            var gateway = new ImmediateGateway(Thread("p.other", false));
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(fallback),
                new NeverDeadline());

            Task<StoryThread> pending = coordinator.PrefetchAsync(
                context,
                CancellationToken.None);
            while (!pending.IsCompleted) yield return null;
            StoryThread result = pending.GetAwaiter().GetResult();

            Assert.AreSame(fallback, result);
            Assert.AreSame(fallback, coordinator.CachedThread);
            Assert.AreEqual("p.test", result.PlayerId);
        }

        [Test]
        public void InvalidContextIsRejectedBeforeGatewayOrCacheKeyUse()
        {
            StoryThreadContext valid = Context();
            var invalid = new StoryThreadContext(
                "not-a-player",
                valid.BundleIdentity,
                valid.Route,
                valid.UpcomingNodeId,
                valid.Nodes,
                valid.MediaAssets);
            var gateway = new FailingGateway();
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(Thread("p.test", true)),
                new NeverDeadline());

            Assert.Throws<StoryThreadContractException>(() =>
                coordinator.PrefetchAsync(invalid, CancellationToken.None));
            Assert.AreEqual(0, gateway.CallCount);
        }

        [Test]
        public void UndefinedRouteIsRejectedBeforeGatewayOrCacheKeyUse()
        {
            StoryThreadContext valid = Context();
            var invalid = new StoryThreadContext(
                valid.PlayerId,
                valid.BundleIdentity,
                (PlayerRoute)999,
                valid.UpcomingNodeId,
                valid.Nodes,
                valid.MediaAssets);
            var gateway = new FailingGateway();
            var coordinator = new StoryThreadPrefetchCoordinator(
                gateway,
                new FixedDefaults(Thread("p.test", true)),
                new NeverDeadline());

            Assert.Throws<StoryThreadContractException>(() =>
                coordinator.PrefetchAsync(invalid, CancellationToken.None));
            Assert.AreEqual(0, gateway.CallCount);
        }

        internal static StoryThreadContext Context()
        {
            var node = new StoryNode { id = "prologue", kind = NodeKind.Narrative };
            node.injectionPoints.Add("node_intro");
            return new StoryThreadContext(
                "p.test",
                new StoryBundleIdentity(
                    "chapter.red_mist",
                    "1.0.0",
                    "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
                PlayerRoute.ShenYan,
                "prologue",
                new Dictionary<string, StoryNode> { { "prologue", node } },
                new Dictionary<string, StoryMediaAsset>());
        }

        internal static StoryThread Thread(string playerId, bool fallback)
        {
            return new StoryThread(
                "1.0.0",
                fallback ? "thr.default.test.v1" : "thr.online.test.v1",
                fallback ? "assoc.default.test.v1" : "assoc.online.test.v1",
                playerId,
                new Dictionary<string, string>(),
                new[]
                {
                    new StoryThreadInjection(
                        "prologue",
                        "node_intro",
                        "technical text",
                        new StoryThreadEffect[0])
                },
                new string[0],
                true,
                "genjob.test.v1",
                fallback);
        }

        private sealed class FixedDefaults : IStoryThreadDefaultCatalog
        {
            private readonly StoryThread _thread;

            public FixedDefaults(StoryThread thread) => _thread = thread;

            public StoryThread GetDefault(StoryThreadContext context) => _thread;
        }

        private sealed class FailingGateway : IStoryThreadGateway
        {
            public int CallCount { get; private set; }

            public Task<StoryThread> GetNextAsync(
                StoryThreadContext context,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromException<StoryThread>(new InvalidOperationException("offline"));
            }
        }

        private sealed class ImmediateGateway : IStoryThreadGateway
        {
            private readonly StoryThread _thread;

            public ImmediateGateway(StoryThread thread) => _thread = thread;
            public int CallCount { get; private set; }

            public Task<StoryThread> GetNextAsync(
                StoryThreadContext context,
                CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(_thread);
            }
        }

        private sealed class BlockingGateway : IStoryThreadGateway
        {
            public bool CancellationObserved { get; private set; }

            public async Task<StoryThread> GetNextAsync(
                StoryThreadContext context,
                CancellationToken cancellationToken)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }
        }

        private sealed class ManuallyCompletedGateway : IStoryThreadGateway
        {
            private readonly TaskCompletionSource<StoryThread> _completion =
                new TaskCompletionSource<StoryThread>();

            public Task<StoryThread> GetNextAsync(
                StoryThreadContext context,
                CancellationToken cancellationToken) => _completion.Task;

            public void Complete(StoryThread thread) => _completion.TrySetResult(thread);
        }

        private sealed class ImmediateDeadline : IStoryThreadDeadline
        {
            public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }

        private sealed class NeverDeadline : IStoryThreadDeadline
        {
            public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
                Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
