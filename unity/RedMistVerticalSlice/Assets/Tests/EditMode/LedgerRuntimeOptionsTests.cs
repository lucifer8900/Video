using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class LedgerRuntimeOptionsTests
    {
        [Test]
        public void CheckedInConfigurationIsOfflineAndReusesNoCredential()
        {
            string root = Path.Combine(Application.dataPath, "StreamingAssets");

            LedgerRuntimeOptions options = LedgerRuntimeOptionsLoader.LoadFromStreamingAssets(root);

            Assert.IsFalse(options.Enabled);
            Assert.IsNull(options.ApiBaseUri);
            Assert.AreEqual(10, options.RequestTimeoutSeconds);
            Assert.AreEqual(262144, options.MaxResponseBytes);
            Assert.AreEqual(25, options.BatchSize);
            Assert.AreEqual(5, options.FlushIntervalSeconds);
            Assert.AreEqual(4096, options.MaxQueuedEvents);
            string checkedIn = File.ReadAllText(Path.Combine(
                root, LedgerRuntimeOptionsLoader.LedgerRelativePath));
            StringAssert.DoesNotContain("key", checkedIn.ToLowerInvariant());
            StringAssert.DoesNotContain("token", checkedIn.ToLowerInvariant());
            StringAssert.DoesNotContain("secret", checkedIn.ToLowerInvariant());
        }

        [TestCase("https://api.example.test/")]
        [TestCase("http://127.0.0.1:5080/")]
        [TestCase("http://localhost:5080/")]
        public void EnabledLedgerUsesSafeApiBaseFromExistingClientConfig(string baseUrl)
        {
            LedgerRuntimeOptions options = LedgerRuntimeOptionsLoader.Parse(
                LedgerJson(true), ClientJson(baseUrl));

            Assert.IsTrue(options.Enabled);
            Assert.AreEqual(baseUrl, options.ApiBaseUri.AbsoluteUri);
        }

        [TestCase("http://api.example.test/")]
        [TestCase("https://user:secret@api.example.test/")]
        [TestCase("https://api.example.test/root/")]
        [TestCase("https://api.example.test/?key=value")]
        [TestCase("https://api.example.test/#fragment")]
        [TestCase("")]
        public void EnabledLedgerRejectsMissingOrUnsafeSharedApiBase(string baseUrl)
        {
            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(LedgerJson(true), ClientJson(baseUrl)));
        }

        [Test]
        public void UnknownMissingOrCredentialLikeConfigurationIsRejected()
        {
            string valid = LedgerJson(true);
            string unknown = valid.Insert(valid.Length - 1, ",\"apiKey\":\"forbidden\"");
            string missing = valid.Replace("\"batchSize\":25,", string.Empty);

            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(unknown, ClientJson("https://api.example.test/")));
            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(missing, ClientJson("https://api.example.test/")));
            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(valid,
                    ClientJson("https://api.example.test/").Insert(
                        ClientJson("https://api.example.test/").Length - 1,
                        ",\"accessToken\":\"forbidden\"")));
        }

        [Test]
        public void BatchTimingCapacityAndDocumentSizeAreBounded()
        {
            string valid = LedgerJson(true);
            AssertInvalid(valid.Replace("\"batchSize\":25", "\"batchSize\":0"));
            AssertInvalid(valid.Replace("\"batchSize\":25", "\"batchSize\":51"));
            AssertInvalid(valid.Replace("\"flushIntervalSeconds\":5", "\"flushIntervalSeconds\":0"));
            AssertInvalid(valid.Replace("\"maxQueuedEvents\":4096", "\"maxQueuedEvents\":100001"));
            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(new string(' ', 65537), ClientJson(string.Empty)));
        }

        [Test]
        public void DisabledConfigurationIgnoresSharedVoiceBaseAndMakesSafeString()
        {
            LedgerRuntimeOptions options = LedgerRuntimeOptionsLoader.Parse(
                LedgerJson(false), ClientJson("https://voice.example.test/"));

            Assert.IsFalse(options.Enabled);
            Assert.IsNull(options.ApiBaseUri);
            StringAssert.DoesNotContain("http", options.ToString().ToLowerInvariant());
        }

        private static void AssertInvalid(string ledgerJson)
        {
            Assert.Throws<LedgerRuntimeConfigurationException>(() =>
                LedgerRuntimeOptionsLoader.Parse(
                    ledgerJson, ClientJson("https://api.example.test/")));
        }

        private static string LedgerJson(bool enabled)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"enabled\":" + enabled.ToString().ToLowerInvariant() + "," +
                   "\"requestTimeoutSeconds\":10," +
                   "\"maxResponseBytes\":262144," +
                   "\"batchSize\":25," +
                   "\"flushIntervalSeconds\":5," +
                   "\"maxQueuedEvents\":4096" +
                   "}";
        }

        private static string ClientJson(string baseUrl)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"asrEnabled\":false," +
                   "\"apiBaseUrl\":\"" + baseUrl + "\"" +
                   "}";
        }
    }
}
