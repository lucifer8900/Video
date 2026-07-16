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
    /// Main-thread, bounded transport for the self-hosted StoryThread endpoint. Redirects are
    /// forbidden and neither query values nor response JSON are written to a log or exception.
    /// </summary>
    public sealed class UnityWebRequestAssociationTransport :
        MonoBehaviour,
        IAssociationHttpTransport
    {
        public const int MaximumRedirects = 0;

        private readonly List<PendingRequest> _pending =
            new List<PendingRequest>();
        private int _unityThreadId;

        private void Awake()
        {
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnDisable()
        {
            AbortPending();
        }

        private void OnDestroy()
        {
            AbortPending();
        }

        public Task<AssociationHttpResponse> SendAsync(
            AssociationHttpRequest model,
            CancellationToken cancellationToken)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureUnityThread();
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeApiUri(model.Url);

            var completion = new TaskCompletionSource<AssociationHttpResponse>();
            StartCoroutine(SendRoutine(model, cancellationToken, completion));
            return completion.Task;
        }

        private IEnumerator SendRoutine(
            AssociationHttpRequest model,
            CancellationToken cancellationToken,
            TaskCompletionSource<AssociationHttpResponse> completion)
        {
            BoundedAssociationDownloadHandler handler = null;
            UnityWebRequest request = null;
            PendingRequest pending = null;
            try
            {
                handler = new BoundedAssociationDownloadHandler(model.MaxResponseBytes);
                request = new UnityWebRequest(
                    model.Url.AbsoluteUri,
                    UnityWebRequest.kHttpVerbGET)
                {
                    downloadHandler = handler,
                    timeout = model.TimeoutSeconds,
                    redirectLimit = MaximumRedirects
                };
                request.SetRequestHeader("Accept", "application/json");
                pending = new PendingRequest(
                    request,
                    () => completion.TrySetCanceled());
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
                    completion.TrySetException(InvalidResponse());
                    yield break;
                }

                AssociationTransportException failure = FailureFor(request);
                if (failure != null)
                {
                    completion.TrySetException(failure);
                    yield break;
                }
                if (request.responseCode < 100 || request.responseCode > 599)
                {
                    completion.TrySetException(InvalidResponse());
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
                completion.TrySetResult(new AssociationHttpResponse(
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

        private void EnsureUnityThread()
        {
            if (_unityThreadId == 0 ||
                Thread.CurrentThread.ManagedThreadId != _unityThreadId)
            {
                throw new InvalidOperationException(
                    "Association networking must start on Unity's main thread.");
            }
        }

        private static void EnsureSafeApiUri(Uri uri)
        {
            if (uri == null ||
                !uri.IsAbsoluteUri ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.Equals(uri.AbsolutePath, "/association/next", StringComparison.Ordinal))
            {
                throw new AssociationTransportException(
                    AssociationTransportFailure.InvalidRequest);
            }

            bool https = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase);
            bool loopbackHttp = string.Equals(
                                    uri.Scheme,
                                    Uri.UriSchemeHttp,
                                    StringComparison.OrdinalIgnoreCase) &&
                                uri.IsLoopback;
            if (!https && !loopbackHttp)
            {
                throw new AssociationTransportException(
                    AssociationTransportFailure.InvalidRequest);
            }
        }

        private static AssociationTransportException FailureFor(
            UnityWebRequest request)
        {
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    return null;
                case UnityWebRequest.Result.ConnectionError:
                    return new AssociationTransportException(
                        AssociationTransportFailure.NetworkUnavailable);
                case UnityWebRequest.Result.ProtocolError:
                    return new AssociationTransportException(
                        AssociationTransportFailure.ServerRejected);
                default:
                    return InvalidResponse();
            }
        }

        private void AbortPending()
        {
            PendingRequest[] snapshot = _pending.ToArray();
            _pending.Clear();
            foreach (PendingRequest request in snapshot)
            {
                request.AbortAndCancel();
            }
        }

        private static AssociationTransportException InvalidResponse()
        {
            return new AssociationTransportException(
                AssociationTransportFailure.InvalidResponse);
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

        private sealed class BoundedAssociationDownloadHandler : DownloadHandlerScript
        {
            private readonly int _maximumBytes;
            private readonly MemoryStream _body;

            public BoundedAssociationDownloadHandler(int maximumBytes)
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
