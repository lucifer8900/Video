using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Main-thread bounded transport for the self-hosted ledger API. It carries no API key,
    /// follows no redirect, and never places request or response JSON in a log message.
    /// </summary>
    public sealed class UnityWebRequestLedgerTransport : MonoBehaviour, ILedgerHttpTransport
    {
        public const int MaximumRedirects = 0;

        private readonly List<PendingRequest> _pending = new List<PendingRequest>();
        private int _unityThreadId;

        private void Awake()
        {
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<LedgerHttpResponse> SendAsync(
            LedgerHttpRequest model,
            CancellationToken cancellationToken)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureUnityThread();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeUri(model.Url);
            var completion = new TaskCompletionSource<LedgerHttpResponse>();
            StartCoroutine(SendRoutine(model, cancellationToken, completion));
            return completion.Task;
        }

        private IEnumerator SendRoutine(
            LedgerHttpRequest model,
            CancellationToken cancellationToken,
            TaskCompletionSource<LedgerHttpResponse> completion)
        {
            byte[] body = null;
            BoundedLedgerDownloadHandler handler = null;
            UnityWebRequest request = null;
            PendingRequest pending = null;
            try
            {
                body = Encoding.UTF8.GetBytes(model.JsonBody);
                handler = new BoundedLedgerDownloadHandler(model.MaxResponseBytes);
                request = new UnityWebRequest(model.Url.AbsoluteUri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = handler,
                    timeout = model.TimeoutSeconds,
                    redirectLimit = MaximumRedirects
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");
                pending = new PendingRequest(request, () => completion.TrySetCanceled());
                _pending.Add(pending);

                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        completion.TrySetCanceled();
                        yield break;
                    }
                    yield return null;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    completion.TrySetCanceled();
                    yield break;
                }
                if (handler.Exceeded)
                {
                    completion.TrySetException(Failure(LedgerTransportFailure.InvalidResponse));
                    yield break;
                }
                LedgerTransportException transportFailure = FailureFor(request);
                if (transportFailure != null)
                {
                    completion.TrySetException(transportFailure);
                    yield break;
                }

                string response;
                try
                {
                    response = handler.GetValidatedText();
                }
                catch (DecoderFallbackException)
                {
                    completion.TrySetException(Failure(LedgerTransportFailure.InvalidResponse));
                    yield break;
                }
                completion.TrySetResult(new LedgerHttpResponse(
                    checked((int)request.responseCode), response));
            }
            finally
            {
                if (pending != null) _pending.Remove(pending);
                if (request != null) request.Dispose();
                else if (handler != null) handler.Dispose();
                if (body != null) Array.Clear(body, 0, body.Length);
            }
        }

        private void EnsureUnityThread()
        {
            if (_unityThreadId == 0) _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_unityThreadId != Thread.CurrentThread.ManagedThreadId || !isActiveAndEnabled)
                throw Failure(LedgerTransportFailure.InvalidRequest);
        }

        private static void EnsureSafeUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw Failure(LedgerTransportFailure.InvalidRequest);
            }
            bool https = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool loopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                                uri.IsLoopback;
            if (!https && !loopbackHttp)
                throw Failure(LedgerTransportFailure.InvalidRequest);
        }

        private static LedgerTransportException FailureFor(UnityWebRequest request)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    if (request.responseCode < 200 || request.responseCode > 299)
                        return Failure(LedgerTransportFailure.ServerRejected);
                    return null;
                case UnityWebRequest.Result.ConnectionError:
                    return Failure(LedgerTransportFailure.NetworkUnavailable);
                case UnityWebRequest.Result.ProtocolError:
                    return Failure(LedgerTransportFailure.ServerRejected);
                default:
                    return Failure(LedgerTransportFailure.InvalidResponse);
            }
        }

        private void AbortPending()
        {
            for (int index = 0; index < _pending.Count; index++)
                _pending[index].AbortAndCancel();
            _pending.Clear();
        }

        private void OnDisable() => AbortPending();
        private void OnDestroy() => AbortPending();

        private static LedgerTransportException Failure(LedgerTransportFailure failure)
        {
            return new LedgerTransportException(failure);
        }

        private sealed class PendingRequest
        {
            private readonly UnityWebRequest _request;
            private readonly Action _cancel;

            public PendingRequest(UnityWebRequest request, Action cancel)
            {
                _request = request;
                _cancel = cancel;
            }

            public void AbortAndCancel()
            {
                _request.Abort();
                _cancel();
            }
        }

        private sealed class BoundedLedgerDownloadHandler : DownloadHandlerScript
        {
            private readonly int _maximumBytes;
            private readonly MemoryStream _body;

            public BoundedLedgerDownloadHandler(int maximumBytes)
                : base(new byte[Math.Min(maximumBytes, 32768)])
            {
                _maximumBytes = maximumBytes;
                _body = new MemoryStream(Math.Min(maximumBytes, 65536));
            }

            public bool Exceeded { get; private set; }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_maximumBytes) Exceeded = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (Exceeded) return false;
                if (data == null || dataLength <= 0) return true;
                if (_body.Length + dataLength > _maximumBytes)
                {
                    Exceeded = true;
                    return false;
                }
                _body.Write(data, 0, dataLength);
                return true;
            }

            public string GetValidatedText()
            {
                return new UTF8Encoding(false, true).GetString(_body.ToArray());
            }
        }
    }
}
