using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public enum LedgerFlushStatus
    {
        Empty,
        Uploaded,
        Deferred
    }

    public sealed class LedgerFlushResult
    {
        private LedgerFlushResult(
            LedgerFlushStatus status,
            int attemptedCount,
            int acknowledgedCount,
            LedgerTransportFailure? failure)
        {
            Status = status;
            AttemptedCount = attemptedCount;
            AcknowledgedCount = acknowledgedCount;
            Failure = failure;
        }

        public LedgerFlushStatus Status { get; }
        public int AttemptedCount { get; }
        public int AcknowledgedCount { get; }
        public LedgerTransportFailure? Failure { get; }

        public static LedgerFlushResult Empty() =>
            new LedgerFlushResult(LedgerFlushStatus.Empty, 0, 0, null);

        public static LedgerFlushResult Uploaded(int attempted, int acknowledged) =>
            new LedgerFlushResult(LedgerFlushStatus.Uploaded, attempted, acknowledged, null);

        public static LedgerFlushResult Deferred(
            int attempted,
            LedgerTransportFailure failure) =>
            new LedgerFlushResult(LedgerFlushStatus.Deferred, attempted, 0, failure);

        public override string ToString()
        {
            return "LedgerFlushResult(Status=" + Status +
                   ", AttemptedCount=" + AttemptedCount +
                   ", AcknowledgedCount=" + AcknowledgedCount + ")";
        }
    }

    public sealed class LedgerUploadCoordinator
    {
        private readonly LedgerOutbox _outbox;
        private readonly ILedgerBatchClient _client;
        private readonly int _batchSize;
        private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);

        public LedgerUploadCoordinator(
            LedgerOutbox outbox,
            ILedgerBatchClient client,
            int batchSize)
        {
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (batchSize < 1 || batchSize > 50)
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            _batchSize = batchSize;
        }

        public LedgerEnqueueResult Enqueue(LedgerEvent item) => _outbox.Enqueue(item);

        public async Task<LedgerFlushResult> FlushOnceAsync(CancellationToken cancellationToken)
        {
            await _flushGate.WaitAsync(cancellationToken);
            try
            {
                IReadOnlyList<LedgerEvent> pending;
                try
                {
                    pending = _outbox.ReadBatch(_batchSize);
                }
                catch (LedgerOutboxException)
                {
                    return LedgerFlushResult.Deferred(0, LedgerTransportFailure.InvalidResponse);
                }
                if (pending.Count == 0) return LedgerFlushResult.Empty();

                string playerId = pending[0].PlayerId;
                var samePlayer = new List<LedgerEvent>();
                for (int index = 0; index < pending.Count; index++)
                {
                    if (string.Equals(pending[index].PlayerId, playerId, StringComparison.Ordinal))
                        samePlayer.Add(pending[index]);
                }

                LedgerUploadResult response;
                try
                {
                    response = await _client.UploadAsync(samePlayer.AsReadOnly(), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (LedgerTransportException error)
                {
                    return LedgerFlushResult.Deferred(samePlayer.Count, error.Failure);
                }

                var keys = new List<LedgerEntryKey>();
                for (int index = 0; index < response.Acknowledgements.Count; index++)
                    keys.Add(response.Acknowledgements[index].Key);
                try
                {
                    _outbox.Acknowledge(keys.AsReadOnly());
                }
                catch (LedgerOutboxException)
                {
                    return LedgerFlushResult.Deferred(
                        samePlayer.Count,
                        LedgerTransportFailure.InvalidResponse);
                }
                return LedgerFlushResult.Uploaded(samePlayer.Count, keys.Count);
            }
            finally
            {
                _flushGate.Release();
            }
        }
    }

    public interface ILedgerUploadSignal
    {
        void Signal();
    }

    /// <summary>
    /// Settlement seam only: callers must supply a fully reviewed LedgerEvent. This class has no
    /// action-label, dialogue, actor, fact, or payload inference logic.
    /// </summary>
    public sealed class LedgerSettlementRecorder
    {
        private readonly LedgerOutbox _outbox;
        private readonly ILedgerUploadSignal _uploadSignal;
        private readonly string _expectedPlayerId;

        public LedgerSettlementRecorder(
            LedgerOutbox outbox,
            ILedgerUploadSignal uploadSignal)
            : this(outbox, uploadSignal, null)
        {
        }

        public LedgerSettlementRecorder(
            LedgerOutbox outbox,
            ILedgerUploadSignal uploadSignal,
            string expectedPlayerId)
        {
            _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            _uploadSignal = uploadSignal ?? throw new ArgumentNullException(nameof(uploadSignal));
            if (expectedPlayerId != null && !LedgerContractRules.IsPlayerId(expectedPlayerId))
                throw new ArgumentException("The expected player identifier is invalid.", nameof(expectedPlayerId));
            _expectedPlayerId = expectedPlayerId;
        }

        public LedgerEnqueueResult Record(LedgerEvent reviewedEvent)
        {
            if (reviewedEvent == null) throw new ArgumentNullException(nameof(reviewedEvent));
            if (_expectedPlayerId != null &&
                !string.Equals(
                    reviewedEvent.PlayerId,
                    _expectedPlayerId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The reviewed ledger event belongs to another player.",
                    nameof(reviewedEvent));
            }
            LedgerEnqueueResult result = _outbox.Enqueue(reviewedEvent);
            if (result == LedgerEnqueueResult.Added) _uploadSignal.Signal();
            return result;
        }
    }
}
