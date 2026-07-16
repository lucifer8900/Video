using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Lingmai.RedMist
{
    public sealed class UnityVoiceInteractionClient : MonoBehaviour, IVoiceRequestCanceller
    {
        public const int RequestTimeoutSeconds = 10;
        public const int MaximumTranscriptCharacters = 4096;

        private const string EndpointPath = "api/v1/voice/interactions";
        private const ulong MaximumResponseBytes = 64 * 1024;
        private static readonly HashSet<string> AllowedConfigProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "asrEnabled",
            "apiBaseUrl"
        };

        private int _generation;
        private bool _enabled;
        private Uri _endpoint;
        private Coroutine _activeCoroutine;
        private UnityWebRequest _activeRequest;
        private byte[] _activeBody;

        public bool IsEnabled => _enabled && _endpoint != null;

        private void Awake()
        {
            ReloadConfiguration();
        }

        public bool ReloadConfiguration()
        {
            Cancel();
            _enabled = false;
            _endpoint = null;
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Config", "client.runtime.json");
                StoryJsonValue root = StoryJson.Parse(File.ReadAllText(path));
                if (root.Kind != StoryJsonKind.Object) return false;
                foreach (string property in root.ObjectValue.Keys)
                {
                    if (!AllowedConfigProperties.Contains(property)) return false;
                }
                if (!TryRequiredString(root, "schemaVersion", out string version) || version != "1.0.0") return false;
                if (!root.TryGetProperty("asrEnabled", out StoryJsonValue enabled) || enabled.Kind != StoryJsonKind.Boolean) return false;
                _enabled = enabled.BooleanValue;
                if (!_enabled) return true;
                if (!TryRequiredString(root, "apiBaseUrl", out string baseUrl) || !TryBuildEndpoint(baseUrl, out _endpoint))
                {
                    _enabled = false;
                    return false;
                }
                return true;
            }
            catch
            {
                _enabled = false;
                _endpoint = null;
                return false;
            }
        }

        public int Interact(
            string nodeId,
            string transcript,
            Action<VoiceInteractionClientResult> completed)
        {
            Cancel();
            int generation = ++_generation;
            if (!IsEnabled || string.IsNullOrWhiteSpace(nodeId) || transcript == null ||
                transcript.Length > MaximumTranscriptCharacters)
            {
                Deliver(completed, VoiceInteractionClientResult.Failed(generation, "interaction_disabled_or_invalid"));
                return generation;
            }

            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new RequestPayload
            {
                nodeId = nodeId,
                transcript = transcript
            }));
            UnityWebRequest request = null;
            try
            {
                request = new UnityWebRequest(_endpoint.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = RequestTimeoutSeconds
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
            }
            catch
            {
                request?.Dispose();
                Clear(body);
                Deliver(completed, VoiceInteractionClientResult.Failed(generation, "request_setup_failed"));
                return generation;
            }

            _activeRequest = request;
            _activeBody = body;
            _activeCoroutine = StartCoroutine(Send(generation, request, body, completed));
            return generation;
        }

        public void Cancel()
        {
            _generation++;
            if (_activeCoroutine != null) StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
            if (_activeRequest != null)
            {
                try { _activeRequest.Abort(); }
                catch { }
                _activeRequest.Dispose();
            }
            _activeRequest = null;
            Clear(_activeBody);
            _activeBody = null;
        }

        private IEnumerator Send(
            int generation,
            UnityWebRequest request,
            byte[] body,
            Action<VoiceInteractionClientResult> completed)
        {
            yield return request.SendWebRequest();
            VoiceInteractionClientResult result = BuildResult(generation, request);
            bool current = generation == _generation && ReferenceEquals(request, _activeRequest);
            if (ReferenceEquals(request, _activeRequest))
            {
                _activeCoroutine = null;
                _activeRequest = null;
                _activeBody = null;
            }
            request.Dispose();
            Clear(body);
            if (current) Deliver(completed, result);
        }

        private static VoiceInteractionClientResult BuildResult(int generation, UnityWebRequest request)
        {
            long status = request.responseCode;
            if (request.result == UnityWebRequest.Result.ConnectionError)
                return VoiceInteractionClientResult.Failed(generation, "network_unavailable", status);
            if (request.result != UnityWebRequest.Result.Success)
                return VoiceInteractionClientResult.Failed(generation, "server_rejected", status);
            if (request.downloadedBytes > MaximumResponseBytes)
                return VoiceInteractionClientResult.Failed(generation, "response_too_large", status);
            return VoiceInteractionResponseParser.TryParse(
                request.downloadHandler.text,
                generation,
                status,
                out VoiceInteractionClientResult result)
                ? result
                : VoiceInteractionClientResult.Failed(generation, "invalid_response", status);
        }

        private static bool TryRequiredString(StoryJsonValue owner, string property, out string value)
        {
            value = string.Empty;
            if (!owner.TryGetProperty(property, out StoryJsonValue item) ||
                item.Kind != StoryJsonKind.String || string.IsNullOrWhiteSpace(item.StringValue)) return false;
            value = item.StringValue;
            return true;
        }

        private static bool TryBuildEndpoint(string baseUrl, out Uri endpoint)
        {
            endpoint = null;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri) ||
                !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment)) return false;
            bool secure = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool loopback = string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && baseUri.IsLoopback;
            if (!secure && !loopback) return false;
            string normalized = baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? baseUri.AbsoluteUri
                : baseUri.AbsoluteUri + "/";
            endpoint = new Uri(new Uri(normalized), EndpointPath);
            return true;
        }

        private static void Deliver(Action<VoiceInteractionClientResult> completed, VoiceInteractionClientResult result)
        {
            if (completed == null) return;
            try { completed(result); }
            catch { }
        }

        private static void Clear(byte[] bytes)
        {
            if (bytes != null && bytes.Length > 0) Array.Clear(bytes, 0, bytes.Length);
        }

        private void OnDisable() => Cancel();
        private void OnDestroy() => Cancel();

        [Serializable]
        private sealed class RequestPayload
        {
            public string nodeId;
            public string transcript;
        }
    }
}
