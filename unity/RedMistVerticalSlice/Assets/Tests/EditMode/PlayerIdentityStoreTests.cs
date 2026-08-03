using System;
using System.IO;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class PlayerIdentityStoreTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "red-mist-player-identity-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void FirstRunCreatesOpaqueGuidAndRestartReturnsSameIdentity()
        {
            Guid fixedGuid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
            var first = new PlayerIdentityStore(_root, () => fixedGuid);

            string created = first.LoadOrCreate();
            var restarted = new PlayerIdentityStore(
                _root,
                () => throw new AssertionException("A persisted identity must be reused."));
            string loaded = restarted.LoadOrCreate();

            Assert.AreEqual("p.0123456789abcdef0123456789abcdef", created);
            Assert.AreEqual(created, loaded);
            string json = File.ReadAllText(Path.Combine(_root, PlayerIdentityStore.FileName));
            Assert.AreEqual(
                "{\"schemaVersion\":\"1.0.0\",\"playerId\":\"" + created + "\"}",
                json);
            StringAssert.DoesNotContain(Environment.MachineName, json);
            StringAssert.DoesNotContain(Environment.UserName, json);
            StringAssert.DoesNotContain("device", json.ToLowerInvariant());
            StringAssert.DoesNotContain("account", json.ToLowerInvariant());
            StringAssert.DoesNotContain("email", json.ToLowerInvariant());
        }

        [Test]
        public void CorruptIdentityFailsClosedAndOriginalFileIsPreserved()
        {
            string path = Path.Combine(_root, PlayerIdentityStore.FileName);
            const string corrupt = "{\"schemaVersion\":\"1.0.0\",\"playerId\":\"device-secret\"}";
            File.WriteAllText(path, corrupt);
            int generated = 0;
            var store = new PlayerIdentityStore(_root, () =>
            {
                generated++;
                return Guid.NewGuid();
            });

            Assert.Throws<PlayerIdentityException>(() => store.LoadOrCreate());

            Assert.AreEqual(0, generated);
            Assert.AreEqual(corrupt, File.ReadAllText(path));
        }

        [TestCase("{\"schemaVersion\":\"1.0.0\",\"playerId\":\"p.ABCDEF0123456789abcdef0123456789\"}")]
        [TestCase("{\"schemaVersion\":\"1.0.0\",\"playerId\":\"p.short\"}")]
        [TestCase("{\"schemaVersion\":\"2.0.0\",\"playerId\":\"p.0123456789abcdef0123456789abcdef\"}")]
        [TestCase("{\"schemaVersion\":\"1.0.0\",\"playerId\":\"p.0123456789abcdef0123456789abcdef\",\"deviceId\":\"x\"}")]
        public void StrictIdentityDocumentRejectsInvalidOrExtraData(string json)
        {
            File.WriteAllText(Path.Combine(_root, PlayerIdentityStore.FileName), json);

            Assert.Throws<PlayerIdentityException>(() =>
                new PlayerIdentityStore(_root).LoadOrCreate());
        }

        [Test]
        public void InterruptedTemporaryFileDoesNotBecomeAnIdentity()
        {
            File.WriteAllText(
                Path.Combine(_root, PlayerIdentityStore.FileName + ".interrupted.tmp"),
                "private-partial-data");
            var store = new PlayerIdentityStore(
                _root,
                () => Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

            string playerId = store.LoadOrCreate();

            Assert.AreEqual("p.aaaaaaaabbbbccccddddeeeeeeeeeeee", playerId);
        }

        [Test]
        public void StringRepresentationRedactsIdentityAndPath()
        {
            var store = new PlayerIdentityStore(
                _root,
                () => Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
            string identity = store.LoadOrCreate();

            string text = store.ToString();

            StringAssert.DoesNotContain(_root, text);
            StringAssert.DoesNotContain(identity, text);
        }
    }
}
