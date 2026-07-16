using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lingmai.RedMist.Tests
{
    public sealed class AssociationRuntimeTests
    {
        private const string ClientDisabled =
            "{\"schemaVersion\":\"1.0.0\",\"asrEnabled\":false,\"apiBaseUrl\":\"\"}";
        private const string ClientLoopback =
            "{\"schemaVersion\":\"1.0.0\",\"asrEnabled\":false,\"apiBaseUrl\":\"http://127.0.0.1:5173/\"}";
        private const string AssociationDisabled =
            "{\"schemaVersion\":\"1.0.0\",\"enabled\":false,\"hardTimeoutSeconds\":8,\"maxResponseBytes\":65536}";

        [Test]
        public void DisabledConfigIsValidWithoutAnApiUrlAndHardDeadlineCannotDrift()
        {
            AssociationRuntimeOptions options = AssociationRuntimeOptionsLoader.Parse(
                AssociationDisabled,
                ClientDisabled);

            Assert.IsFalse(options.Enabled);
            Assert.IsNull(options.ApiBaseUri);
            Assert.AreEqual(8, options.HardTimeoutSeconds);
            Assert.AreEqual(65536, options.MaxResponseBytes);
            Assert.Throws<AssociationRuntimeConfigurationException>(() =>
                AssociationRuntimeOptionsLoader.Parse(
                    AssociationDisabled.Replace("\"hardTimeoutSeconds\":8", "\"hardTimeoutSeconds\":7"),
                    ClientDisabled));
            Assert.Throws<AssociationRuntimeConfigurationException>(() =>
                AssociationRuntimeOptionsLoader.Parse(
                    AssociationDisabled.Replace("65536", "65537"),
                    ClientDisabled));
        }

        [UnityTest]
        public IEnumerator ApiClientUsesGetAssociationNextAndStrictlyValidatesResponse()
        {
            AssociationRuntimeOptions options = AssociationRuntimeOptionsLoader.Parse(
                AssociationDisabled.Replace("\"enabled\":false", "\"enabled\":true"),
                ClientLoopback);
            StoryThreadContext context = StoryThreadPrefetchCoordinatorTests.Context();
            var transport = new RecordingTransport(new AssociationHttpResponse(
                200,
                BoundResponse(context, StoryThreadCodecTests.ValidJson("[]"))));
            var client = new AssociationApiClient(options, transport);

            Task<StoryThread> pending = client.GetNextAsync(context, CancellationToken.None);
            while (!pending.IsCompleted) yield return null;
            StoryThread result = pending.GetAwaiter().GetResult();

            Assert.AreEqual("GET", transport.Request.Method);
            Assert.AreEqual("/association/next", transport.Request.Url.AbsolutePath);
            StringAssert.Contains("playerId=p.test", transport.Request.Url.Query);
            StringAssert.Contains("bundleContentHash=sha256%3A", transport.Request.Url.Query);
            Assert.AreEqual("thr.test.v1", result.ThreadId);
        }

        [UnityTest]
        public IEnumerator ApiClientRejectsAThreadEnvelopeBoundToAnotherBundle()
        {
            AssociationRuntimeOptions options = AssociationRuntimeOptionsLoader.Parse(
                AssociationDisabled.Replace("\"enabled\":false", "\"enabled\":true"),
                ClientLoopback);
            StoryThreadContext context = StoryThreadPrefetchCoordinatorTests.Context();
            string response = BoundResponse(
                context,
                StoryThreadCodecTests.ValidJson("[]")).Replace(
                context.BundleIdentity.ContentHash,
                "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
            var transport = new RecordingTransport(new AssociationHttpResponse(200, response));
            var client = new AssociationApiClient(options, transport);

            Task<StoryThread> pending = client.GetNextAsync(context, CancellationToken.None);
            while (!pending.IsCompleted) yield return null;

            AssociationTransportException error = Assert.Throws<AssociationTransportException>(
                () => pending.GetAwaiter().GetResult());
            Assert.AreEqual(AssociationTransportFailure.InvalidResponse, error.Failure);
        }

        [UnityTest]
        public IEnumerator ApiClientRejectsAThreadEnvelopeBoundToAnotherUpcomingNode()
        {
            AssociationRuntimeOptions options = AssociationRuntimeOptionsLoader.Parse(
                AssociationDisabled.Replace("\"enabled\":false", "\"enabled\":true"),
                ClientLoopback);
            StoryThreadContext original = StoryThreadPrefetchCoordinatorTests.Context();
            var flight = new StoryNode { id = "flight", kind = NodeKind.Narrative };
            flight.injectionPoints.Add("travel_event");
            var nodes = new Dictionary<string, StoryNode>(StringComparer.Ordinal)
            {
                { "prologue", original.Nodes["prologue"] },
                { "flight", flight }
            };
            var requested = new StoryThreadContext(
                original.PlayerId,
                original.BundleIdentity,
                original.Route,
                "flight",
                nodes,
                original.MediaAssets);
            var stale = new StoryThreadContext(
                original.PlayerId,
                original.BundleIdentity,
                original.Route,
                "prologue",
                nodes,
                original.MediaAssets);
            var transport = new RecordingTransport(new AssociationHttpResponse(
                200,
                BoundResponse(stale, StoryThreadCodecTests.ValidJson("[]"))));
            var client = new AssociationApiClient(options, transport);

            Task<StoryThread> pending = client.GetNextAsync(
                requested,
                CancellationToken.None);
            while (!pending.IsCompleted) yield return null;

            AssociationTransportException error =
                Assert.Throws<AssociationTransportException>(
                    () => pending.GetAwaiter().GetResult());
            Assert.AreEqual(AssociationTransportFailure.InvalidResponse, error.Failure);
        }

        [Test]
        public void DisabledBootstrapNeverInstallsNetworkTransport()
        {
            int calls = 0;
            AssociationRuntimeContext context = AssociationRuntimeBootstrap.Initialize(
                AssociationRuntimeOptionsLoader.Parse(AssociationDisabled, ClientDisabled),
                StoryThreadPrefetchCoordinatorTests.Context(),
                new FixedDefaultCatalog(StoryThreadPrefetchCoordinatorTests.Thread("p.test", true)),
                () =>
                {
                    calls++;
                    return new RecordingTransport(new AssociationHttpResponse(500, "{}"));
                });

            Assert.AreEqual(0, calls);
            Assert.IsFalse(context.OnlineEnabled);
        }

        [Test]
        public void OfflineSharedIdentityCanNeverInstallTransportEvenIfConfigIsEnabled()
        {
            int calls = 0;
            AssociationRuntimeOptions options = AssociationRuntimeOptionsLoader.Parse(
                AssociationDisabled.Replace("\"enabled\":false", "\"enabled\":true"),
                ClientLoopback);
            StoryThreadContext original = StoryThreadPrefetchCoordinatorTests.Context();
            var offlineContext = new StoryThreadContext(
                StoryThreadDefaults.OfflinePlayerId,
                original.BundleIdentity,
                original.Route,
                original.UpcomingNodeId,
                original.Nodes,
                original.MediaAssets);

            AssociationRuntimeContext runtime = AssociationRuntimeBootstrap.Initialize(
                options,
                offlineContext,
                new FixedDefaultCatalog(
                    StoryThreadPrefetchCoordinatorTests.Thread(
                        StoryThreadDefaults.OfflinePlayerId,
                        true)),
                () =>
                {
                    calls++;
                    return new RecordingTransport(new AssociationHttpResponse(500, "{}"));
                });

            Assert.AreEqual(0, calls);
            Assert.IsFalse(runtime.OnlineEnabled);
        }

        [Test]
        public void ProductionDefaultIsHashBoundAndContainsOnlyApprovedTextWithoutEffectsOrMedia()
        {
            string storyRoot = Path.Combine(
                Application.streamingAssetsPath,
                "Story",
                "red-mist");
            StoryBundleLoadResult loaded = StoryBundleLoader.LoadFromFile(Path.Combine(
                storyRoot,
                StoryBundleLoader.BundleFileName));
            Assert.IsTrue(loaded.Success, loaded.TechnicalMessage);
            var context = new StoryThreadContext(
                "p.test",
                loaded.Bundle.Identity,
                PlayerRoute.ShenYan,
                loaded.Bundle.EntryNodeId,
                loaded.Bundle.Nodes,
                loaded.Bundle.MediaAssets);
            string defaultsPath = Path.Combine(storyRoot, "association.defaults.json");
            string actualDefaultsHash = StoryBundleLoader.ComputeContentHash(
                File.ReadAllText(defaultsPath));
            Assert.AreEqual(
                StoryThreadDefaults.ApprovedRedMistDefaultsHash,
                actualDefaultsHash,
                "The checked-in approved default must match its code-level trust anchor. " +
                "actual=" + actualDefaultsHash);
            var catalog = new FileStoryThreadDefaultCatalog(
                defaultsPath,
                StoryThreadDefaults.ApprovedRedMistDefaultsHash);

            StoryThread thread = catalog.GetDefault(context);

            Assert.NotNull(thread);
            Assert.AreEqual("p.test", thread.PlayerId);
            Assert.IsTrue(thread.FallbackUsed);
            Assert.AreEqual(3, thread.Injections.Count);
            CollectionAssert.AreEqual(
                new[]
                {
                    "赤雾沿石阶缓缓回卷，雾门开启前，营地里的交谈声一层层低了下去。",
                    "一阵横风切过狭隘，低空雾流骤然翻卷，飞行队形被迫收紧。",
                    "石峻瞥了一眼来路，又把目光落回阵钉上，像是在等你先暴露判断。"
                },
                new[]
                {
                    thread.Injections[0].Text,
                    thread.Injections[1].Text,
                    thread.Injections[2].Text
                });
            foreach (StoryThreadInjection injection in thread.Injections)
                Assert.IsEmpty(injection.Effects);
            Assert.IsEmpty(thread.MediaRefs);

            string repositoryCopy = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "..",
                "..",
                "content",
                "story",
                "red-mist",
                "association.defaults.json"));
            CollectionAssert.AreEqual(
                File.ReadAllBytes(repositoryCopy),
                File.ReadAllBytes(Path.Combine(storyRoot, "association.defaults.json")),
                "The compiler-authority default and Unity shipping copy must be byte-identical.");
        }

        [Test]
        public void ProductionDefaultFailsClosedForAnotherBundleHash()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "Story",
                "red-mist",
                "association.defaults.json");
            var catalog = new FileStoryThreadDefaultCatalog(
                path,
                StoryThreadDefaults.ApprovedRedMistDefaultsHash);

            Assert.IsNull(catalog.GetDefault(StoryThreadPrefetchCoordinatorTests.Context()));
        }

        [Test]
        public void ProductionDefaultRejectsSchemaValidTextTamperingBeforeAdoption()
        {
            string storyRoot = Path.Combine(
                Application.streamingAssetsPath,
                "Story",
                "red-mist");
            StoryBundleLoadResult loaded = StoryBundleLoader.LoadFromFile(Path.Combine(
                storyRoot,
                StoryBundleLoader.BundleFileName));
            Assert.IsTrue(loaded.Success, loaded.TechnicalMessage);
            var context = new StoryThreadContext(
                "p.test",
                loaded.Bundle.Identity,
                PlayerRoute.ShenYan,
                loaded.Bundle.EntryNodeId,
                loaded.Bundle.Nodes,
                loaded.Bundle.MediaAssets);
            string source = Path.Combine(storyRoot, "association.defaults.json");
            string temporary = Path.Combine(
                Application.temporaryCachePath,
                "cx405-default-tamper-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string changed = File.ReadAllText(source).Replace(
                    "赤雾沿石阶缓缓回卷",
                    "赤雾顺石阶缓缓回卷");
                Assert.AreNotEqual(File.ReadAllText(source), changed);
                File.WriteAllText(temporary, changed);
                var catalog = new FileStoryThreadDefaultCatalog(
                    temporary,
                    StoryThreadDefaults.ApprovedRedMistDefaultsHash);

                Assert.IsNull(catalog.GetDefault(context));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private sealed class RecordingTransport : IAssociationHttpTransport
        {
            private readonly AssociationHttpResponse _response;

            public RecordingTransport(AssociationHttpResponse response) => _response = response;
            public AssociationHttpRequest Request { get; private set; }

            public Task<AssociationHttpResponse> SendAsync(
                AssociationHttpRequest request,
                CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(_response);
            }
        }

        private static string BoundResponse(
            StoryThreadContext context,
            string threadJson)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"chapterId\":\"" + context.BundleIdentity.BundleId + "\"," +
                   "\"bundleVersion\":\"" + context.BundleIdentity.Version + "\"," +
                   "\"bundleContentHash\":\"" + context.BundleIdentity.ContentHash + "\"," +
                   "\"route\":\"" + context.Route + "\"," +
                   "\"upcomingNodeId\":\"" + context.UpcomingNodeId + "\"," +
                   "\"thread\":" + threadJson +
                   "}";
        }

        private sealed class FixedDefaultCatalog : IStoryThreadDefaultCatalog
        {
            private readonly StoryThread _thread;

            public FixedDefaultCatalog(StoryThread thread) => _thread = thread;
            public StoryThread GetDefault(StoryThreadContext context) => _thread;
        }
    }
}
