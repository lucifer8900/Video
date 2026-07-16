using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Lingmai.RedMist.Tests
{
    public sealed class GenerationRuntimeOptionsTests
    {
        [Test]
        public void CheckedInConfigurationIsStrictlyOfflineByDefault()
        {
            string streamingAssetsRoot = Path.Combine(Application.dataPath, "StreamingAssets");

            GenerationRuntimeOptions options =
                GenerationRuntimeOptionsLoader.LoadFromStreamingAssets(streamingAssetsRoot);

            Assert.IsFalse(options.Enabled);
            Assert.IsNull(options.ApiBaseUri);
            Assert.IsEmpty(options.AllowedMediaHosts);
            Assert.AreEqual(10, options.RequestTimeoutSeconds);
            Assert.AreEqual(262144, options.MaxResponseBytes);
            Assert.AreEqual(536870912L, options.MaxDownloadBytes);
        }

        [TestCase("https://api.example.test/")]
        [TestCase("http://127.0.0.1:5080/")]
        [TestCase("http://localhost:5080/")]
        public void EnabledConfigurationAcceptsHttpsOrExplicitLoopback(string apiBaseUrl)
        {
            GenerationRuntimeOptions options = GenerationRuntimeOptionsLoader.Parse(
                EnabledJson(apiBaseUrl, "media.example.test"));

            Assert.IsTrue(options.Enabled);
            Assert.AreEqual(apiBaseUrl, options.ApiBaseUri.AbsoluteUri);
            CollectionAssert.AreEqual(
                new[] { "media.example.test" },
                options.AllowedMediaHosts);
        }

        [TestCase("http://api.example.test/")]
        [TestCase("https://user:secret@api.example.test/")]
        [TestCase("https://api.example.test/root/")]
        [TestCase("https://api.example.test/?secret=value")]
        [TestCase("https://api.example.test/#fragment")]
        public void EnabledConfigurationRejectsUnsafeApiBaseUrl(string apiBaseUrl)
        {
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(
                    EnabledJson(apiBaseUrl, "media.example.test")));
        }

        [TestCase("*.example.test")]
        [TestCase("https://media.example.test")]
        [TestCase("media.example.test:443")]
        [TestCase("user@media.example.test")]
        [TestCase("")]
        public void MediaAllowlistRequiresExactDnsHosts(string host)
        {
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(
                    EnabledJson("https://api.example.test/", host)));
        }

        [Test]
        public void UnknownOrMissingPropertiesAreRejected()
        {
            string valid = EnabledJson("https://api.example.test/", "media.example.test");
            string unknown = valid.Replace(
                "\"maxDownloadBytes\":536870912",
                "\"maxDownloadBytes\":536870912,\"apiKey\":\"must-not-exist\"");
            string missing = valid.Replace("\"maxResponseBytes\":262144,", string.Empty);

            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(unknown));
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(missing));
        }

        [Test]
        public void DuplicateHostsAndUnboundedLimitsAreRejected()
        {
            string duplicate = EnabledJson(
                "https://api.example.test/",
                "media.example.test\",\"MEDIA.example.test");
            string hugeResponse = EnabledJson(
                "https://api.example.test/",
                "media.example.test").Replace("262144", "999999999");
            string hugeDownload = EnabledJson(
                "https://api.example.test/",
                "media.example.test").Replace("536870912", "999999999999");

            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(duplicate));
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(hugeResponse));
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(hugeDownload));
        }

        [Test]
        public void OversizedConfigurationIsRejectedBeforeParsing()
        {
            Assert.Throws<GenerationRuntimeConfigurationException>(
                () => GenerationRuntimeOptionsLoader.Parse(new string(' ', 65537)));
        }

        private static string EnabledJson(string apiBaseUrl, string mediaHost)
        {
            return "{" +
                   "\"schemaVersion\":\"1.0.0\"," +
                   "\"enabled\":true," +
                   "\"apiBaseUrl\":\"" + apiBaseUrl + "\"," +
                   "\"allowedMediaHosts\":[\"" + mediaHost + "\"]," +
                   "\"requestTimeoutSeconds\":10," +
                   "\"maxResponseBytes\":262144," +
                   "\"maxDownloadBytes\":536870912" +
                   "}";
        }
    }
}
