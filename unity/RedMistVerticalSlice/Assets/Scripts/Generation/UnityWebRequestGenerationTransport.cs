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
    /// Main-thread UnityWebRequest transport. It is intentionally a component so cancellation
    /// polling and Abort both run on Unity's thread. No error path reads or logs a signed URL.
    /// </summary>
    public sealed class UnityWebRequestGenerationTransport : MonoBehaviour, IGenerationHttpTransport
    {
        public const int MaximumRedirects = 0;

        private readonly List<PendingOperation> _pending = new List<PendingOperation>();
        private int _unityThreadId;

        private void Awake()
        {
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnDisable()
        {
            AbortPendingOperations();
        }

        private void OnDestroy()
        {
            AbortPendingOperations();
        }

        public Task<GenerationHttpResponse> SendAsync(
            GenerationHttpRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureUnityThread();
            cancellationToken.ThrowIfCancellationRequested();

            var completion = new TaskCompletionSource<GenerationHttpResponse>();
            StartCoroutine(SendRoutine(request, cancellationToken, completion));
            return completion.Task;
        }

        public Task DownloadAsync(
            GenerationDownloadRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureUnityThread();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeDownloadUri(request.Url);

            var completion = new TaskCompletionSource<bool>();
            StartCoroutine(DownloadRoutine(request, cancellationToken, completion));
            return completion.Task;
        }

        private IEnumerator SendRoutine(
            GenerationHttpRequest model,
            CancellationToken cancellationToken,
            TaskCompletionSource<GenerationHttpResponse> completion)
        {
            BoundedDownloadHandler handler = null;
            UnityWebRequest request = null;
            PendingOperation pending = null;
            try
            {
                handler = new BoundedDownloadHandler(model.MaxResponseBytes);
                request = new UnityWebRequest(model.Url.AbsoluteUri, model.Method)
                {
                    downloadHandler = handler,
                    timeout = model.TimeoutSeconds,
                    redirectLimit = MaximumRedirects
                };
                if (string.Equals(model.Method, "POST", StringComparison.Ordinal))
                {
                    request.uploadHandler = new UploadHandlerRaw(
                        Encoding.UTF8.GetBytes(model.JsonBody));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.SetRequestHeader("Accept", "application/json");

                pending = Register(
                    request,
                    () => completion.TrySetCanceled());
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
                    completion.TrySetException(InvalidResponse());
                    yield break;
                }

                GenerationTransportException failure = FailureFor(request);
                if (failure != null)
                {
                    completion.TrySetException(failure);
                    yield break;
                }

                string body;
                try
                {
                    body = handler.GetValidatedText();
                }
                catch (DecoderFallbackException)
                {
                    completion.TrySetException(InvalidResponse());
                    yield break;
                }
                completion.TrySetResult(new GenerationHttpResponse(
                    checked((int)request.responseCode),
                    body));
            }
            finally
            {
                if (pending != null) _pending.Remove(pending);
                if (request != null) request.Dispose();
                else if (handler != null) handler.Dispose();
            }
        }

        private IEnumerator DownloadRoutine(
            GenerationDownloadRequest model,
            CancellationToken cancellationToken,
            TaskCompletionSource<bool> completion)
        {
            UnityWebRequest request = null;
            PendingOperation pending = null;
            try
            {
                var downloadHandler = new DownloadHandlerFile(model.DestinationPath, false)
                {
                    removeFileOnAbort = true
                };
                request = new UnityWebRequest(model.Url.AbsoluteUri, UnityWebRequest.kHttpVerbGET)
                {
                    downloadHandler = downloadHandler,
                    timeout = model.TimeoutSeconds,
                    redirectLimit = MaximumRedirects
                };
                request.SetRequestHeader("Accept", "video/mp4");

                pending = Register(
                    request,
                    () => completion.TrySetCanceled());
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        request.Abort();
                        completion.TrySetCanceled();
                        yield break;
                    }
                    if ((long)request.downloadedBytes > model.MaxBytes)
                    {
                        request.Abort();
                        completion.TrySetException(InvalidResponse());
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

                GenerationTransportException failure = FailureFor(request);
                if (failure != null)
                {
                    completion.TrySetException(failure);
                    yield break;
                }
                if ((long)request.downloadedBytes != model.ExpectedBytes ||
                    (long)request.downloadedBytes > model.MaxBytes)
                {
                    completion.TrySetException(InvalidResponse());
                    yield break;
                }
                completion.TrySetResult(true);
            }
            finally
            {
                if (pending != null) _pending.Remove(pending);
                if (request != null) request.Dispose();
            }
        }

        private PendingOperation Register(UnityWebRequest request, Action cancel)
        {
            var pending = new PendingOperation(request, cancel);
            _pending.Add(pending);
            return pending;
        }

        private void AbortPendingOperations()
        {
            for (int index = 0; index < _pending.Count; index++)
            {
                _pending[index].AbortAndCancel();
            }
            _pending.Clear();
        }

        private void EnsureUnityThread()
        {
            if (_unityThreadId == 0) _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            if (_unityThreadId != Thread.CurrentThread.ManagedThreadId || !isActiveAndEnabled)
                throw InvalidResponse();
        }

        private static void EnsureSafeDownloadUri(Uri uri)
        {
            if (uri == null ||
                !uri.IsAbsoluteUri ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !uri.IsDefaultPort)
            {
                throw InvalidResponse();
            }
        }

        private static GenerationTransportException FailureFor(UnityWebRequest request)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    if (request.responseCode < 200 || request.responseCode > 299)
                        return ServerRejected();
                    return null;
                case UnityWebRequest.Result.ConnectionError:
                    return new GenerationTransportException(
                        GenerationTransportFailure.NetworkUnavailable);
                case UnityWebRequest.Result.ProtocolError:
                    return ServerRejected();
                case UnityWebRequest.Result.DataProcessingError:
                    return InvalidResponse();
                default:
                    return InvalidResponse();
            }
        }

        private static GenerationTransportException InvalidResponse()
        {
            return new GenerationTransportException(GenerationTransportFailure.InvalidResponse);
        }

        private static GenerationTransportException ServerRejected()
        {
            return new GenerationTransportException(GenerationTransportFailure.ServerRejected);
        }

        private sealed class PendingOperation
        {
            private readonly UnityWebRequest _request;
            private readonly Action _cancel;

            public PendingOperation(UnityWebRequest request, Action cancel)
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

        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private readonly long _maxBytes;
            private readonly MemoryStream _body;

            public BoundedDownloadHandler(int maxBytes)
                : base(new byte[Math.Min(maxBytes, 32768)])
            {
                _maxBytes = maxBytes;
                _body = new MemoryStream(Math.Min(maxBytes, 65536));
            }

            public bool Exceeded { get; private set; }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_maxBytes) Exceeded = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (Exceeded) return false;
                if (data == null || dataLength <= 0) return true;
                if (_body.Length + dataLength > _maxBytes)
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
