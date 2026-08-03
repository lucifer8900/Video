using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Lingmai.RedMist
{
    public sealed class AsrClientResult
    {
        private AsrClientResult(
            int generation,
            bool success,
            string text,
            string requestId,
            string errorCode,
            long httpStatusCode)
        {
            Generation = generation;
            Success = success;
            Text = text ?? string.Empty;
            RequestId = requestId ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            HttpStatusCode = httpStatusCode;
        }

        public int Generation { get; }
        public bool Success { get; }
        public string Text { get; }
        public string RequestId { get; }
        public string ErrorCode { get; }
        public long HttpStatusCode { get; }

        internal static AsrClientResult Succeeded(
            int generation,
            string text,
            string requestId,
            long httpStatusCode) =>
            new AsrClientResult(generation, true, text, requestId, string.Empty, httpStatusCode);

        internal static AsrClientResult Failed(
            int generation,
            string errorCode,
            long httpStatusCode = 0) =>
            new AsrClientResult(generation, false, string.Empty, string.Empty, errorCode, httpStatusCode);
    }

    /// <summary>
    /// Sends one bounded in-memory WAV to the game's own API. It contains no vendor model, key,
    /// retry policy, file storage, or raw-audio logging.
    /// </summary>
    public sealed class UnityAsrClient : MonoBehaviour, IVoiceRequestCanceller
    {
        public const int RequestTimeoutSeconds = 10;

        private const string SupportedConfigVersion = "1.0.0";
        private const string EndpointPath = "api/v1/asr/transcriptions";
        private const ulong MaximumResponseBytes = 64 * 1024;

        private static readonly HashSet<string> AllowedConfigProperties =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "schemaVersion",
                "asrEnabled",
                "apiBaseUrl"
            };

        private int _generation;
        private bool _configurationLoaded;
        private bool _asrEnabled;
        private Uri _endpoint;
        private string _configurationError = string.Empty;
        private Coroutine _activeCoroutine;
        private UnityWebRequest _activeRequest;
        private byte[] _activeWavBytes;

        public int CurrentGeneration => _generation;
        public bool IsEnabled => _configurationLoaded && _asrEnabled && _endpoint != null;
        public bool IsBusy => _activeRequest != null;
        public string ConfigurationError => _configurationError;

        private void Awake()
        {
            ReloadConfiguration();
        }

        public bool ReloadConfiguration()
        {
            Cancel();
            _configurationLoaded = true;
            _asrEnabled = false;
            _endpoint = null;
            _configurationError = string.Empty;

            string path = Path.Combine(
                Application.streamingAssetsPath,
                "Config",
                "client.runtime.json");

            string json;
            try
            {
                if (!File.Exists(path))
                {
                    _configurationError = "ASR configuration file is unavailable.";
                    return false;
                }
                json = File.ReadAllText(path);
            }
            catch
            {
                _configurationError = "ASR configuration file could not be read.";
                return false;
            }

            try
            {
                StoryJsonValue root = StoryJson.Parse(json);
                if (root.Kind != StoryJsonKind.Object)
                    return ConfigurationFailure("ASR configuration root must be an object.");

                foreach (string property in root.ObjectValue.Keys)
                {
                    if (!AllowedConfigProperties.Contains(property))
                        return ConfigurationFailure("ASR configuration contains an unsupported property.");
                }

                string version = RequiredString(root, "schemaVersion");
                if (!string.Equals(version, SupportedConfigVersion, StringComparison.Ordinal))
                    return ConfigurationFailure("ASR configuration version is unsupported.");

                if (!root.TryGetProperty("asrEnabled", out StoryJsonValue enabled) ||
                    enabled.Kind != StoryJsonKind.Boolean)
                {
                    return ConfigurationFailure("ASR configuration must declare asrEnabled.");
                }

                _asrEnabled = enabled.BooleanValue;
                if (!_asrEnabled) return true;

                string baseUrl = RequiredString(root, "apiBaseUrl");
                if (!TryBuildEndpoint(baseUrl, out Uri endpoint))
                    return ConfigurationFailure("ASR API base URL is not allowed.");

                _endpoint = endpoint;
                return true;
            }
            catch
            {
                return ConfigurationFailure("ASR configuration JSON is invalid.");
            }
        }

        public int Transcribe(
            VoiceAudioPayload audio,
            Action<AsrClientResult> completed)
        {
            if (!_configurationLoaded) ReloadConfiguration();
            Cancel();
            int generation = ++_generation;

            if (!IsEnabled)
            {
                Deliver(completed, AsrClientResult.Failed(generation, "asr_disabled"));
                return generation;
            }

            if (!Pcm16WavEncoder.TryEncode(audio, out byte[] wavBytes, out _))
            {
                Deliver(completed, AsrClientResult.Failed(generation, "invalid_audio"));
                return generation;
            }

            string requestId = Guid.NewGuid().ToString("N");
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(_endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(wavBytes),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = RequestTimeoutSeconds
                };
                request.SetRequestHeader("Content-Type", "audio/wav");
                request.SetRequestHeader("Accept", "application/json");
                request.SetRequestHeader("Idempotency-Key", requestId);
                request.SetRequestHeader("X-Request-Id", requestId);
            }
            catch
            {
                request?.Dispose();
                Clear(wavBytes);
                Deliver(completed, AsrClientResult.Failed(generation, "request_setup_failed"));
                return generation;
            }

            _activeRequest = request;
            _activeWavBytes = wavBytes;
            _activeCoroutine = StartCoroutine(Send(generation, requestId, request, wavBytes, completed));
            return generation;
        }

        public void Cancel()
        {
            _generation++;

            Coroutine coroutine = _activeCoroutine;
            _activeCoroutine = null;
            if (coroutine != null) StopCoroutine(coroutine);

            UnityWebRequest request = _activeRequest;
            _activeRequest = null;
            if (request != null)
            {
                try { request.Abort(); }
                catch { }
                request.Dispose();
            }

            byte[] wavBytes = _activeWavBytes;
            _activeWavBytes = null;
            Clear(wavBytes);
        }

        private IEnumerator Send(
            int generation,
            string requestId,
            UnityWebRequest request,
            byte[] wavBytes,
            Action<AsrClientResult> completed)
        {
            yield return request.SendWebRequest();

            AsrClientResult result = BuildResult(generation, requestId, request);
            bool isCurrent = generation == _generation && ReferenceEquals(request, _activeRequest);
            if (ReferenceEquals(request, _activeRequest))
            {
                _activeCoroutine = null;
                _activeRequest = null;
                _activeWavBytes = null;
            }

            request.Dispose();
            Clear(wavBytes);

            if (isCurrent) Deliver(completed, result);
        }

        private static AsrClientResult BuildResult(
            int generation,
            string localRequestId,
            UnityWebRequest request)
        {
            long statusCode = request.responseCode;
            if (request.result == UnityWebRequest.Result.ConnectionError)
                return AsrClientResult.Failed(generation, "network_unavailable", statusCode);
            if (request.result == UnityWebRequest.Result.ProtocolError)
                return AsrClientResult.Failed(generation, "server_rejected", statusCode);
            if (request.result == UnityWebRequest.Result.DataProcessingError)
                return AsrClientResult.Failed(generation, "invalid_response", statusCode);
            if (request.result != UnityWebRequest.Result.Success)
                return AsrClientResult.Failed(generation, "request_failed", statusCode);
            if (request.downloadedBytes > MaximumResponseBytes)
                return AsrClientResult.Failed(generation, "response_too_large", statusCode);

            try
            {
                StoryJsonValue root = StoryJson.Parse(request.downloadHandler.text);
                if (root.Kind != StoryJsonKind.Object)
                    return AsrClientResult.Failed(generation, "invalid_response", statusCode);

                string text = RequiredString(root, "text");
                if (text.Length > 4096)
                    return AsrClientResult.Failed(generation, "invalid_response", statusCode);

                string requestId = OptionalString(root, "requestId");
                if (string.IsNullOrEmpty(requestId)) requestId = localRequestId;
                return AsrClientResult.Succeeded(generation, text, requestId, statusCode);
            }
            catch
            {
                return AsrClientResult.Failed(generation, "invalid_response", statusCode);
            }
        }

        private static string RequiredString(StoryJsonValue owner, string property)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value) ||
                value.Kind != StoryJsonKind.String ||
                string.IsNullOrWhiteSpace(value.StringValue))
            {
                throw new InvalidDataException("Missing required string: " + property);
            }
            return value.StringValue;
        }

        private static string OptionalString(StoryJsonValue owner, string property)
        {
            if (!owner.TryGetProperty(property, out StoryJsonValue value)) return string.Empty;
            if (value.Kind != StoryJsonKind.String)
                throw new InvalidDataException("Invalid string: " + property);
            return value.StringValue ?? string.Empty;
        }

        private static bool TryBuildEndpoint(string baseUrl, out Uri endpoint)
        {
            endpoint = null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri)) return false;
            if (!string.IsNullOrEmpty(baseUri.UserInfo) ||
                !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment))
            {
                return false;
            }

            bool secure = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool loopbackDevelopment =
                string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                baseUri.IsLoopback;
            if (!secure && !loopbackDevelopment) return false;

            string normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? baseUri.AbsoluteUri
                : baseUri.AbsoluteUri + "/";
            endpoint = new Uri(new Uri(normalized, UriKind.Absolute), EndpointPath);
            return true;
        }

        private bool ConfigurationFailure(string message)
        {
            _asrEnabled = false;
            _endpoint = null;
            _configurationError = message;
            return false;
        }

        private static void Deliver(Action<AsrClientResult> completed, AsrClientResult result)
        {
            if (completed == null) return;
            try { completed(result); }
            catch { }
        }

        private static void Clear(byte[] bytes)
        {
            if (bytes != null && bytes.Length > 0) Array.Clear(bytes, 0, bytes.Length);
        }

        private void OnDisable()
        {
            Cancel();
        }

        private void OnDestroy()
        {
            Cancel();
        }
    }
}
