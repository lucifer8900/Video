using System;
using System.Threading;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Periodic reconnect pump. It attempts at most one flush at a time and never logs event or
    /// transport content. All failure outcomes remain durable in the outbox for a later attempt.
    /// </summary>
    public sealed class LedgerUploadPump : MonoBehaviour, ILedgerUploadSignal
    {
        private LedgerUploadCoordinator _coordinator;
        private int _intervalSeconds;
        private bool _pendingSignal;
        private bool _running;
        private float _nextAttemptAt;
        private int _generation;
        private CancellationTokenSource _cancellation;

        public void Initialize(LedgerUploadCoordinator coordinator, int intervalSeconds)
        {
            if (_coordinator != null) throw new InvalidOperationException(
                "The ledger upload pump is already initialized.");
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            if (intervalSeconds < 1 || intervalSeconds > 3600)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            _intervalSeconds = intervalSeconds;
            _pendingSignal = true;
            _nextAttemptAt = Time.unscaledTime;
            _cancellation = new CancellationTokenSource();
        }

        public void Signal()
        {
            if (_coordinator != null) _pendingSignal = true;
        }

        private void Update()
        {
            if (_coordinator == null || _running || !isActiveAndEnabled) return;
            if (!_pendingSignal && Time.unscaledTime < _nextAttemptAt) return;
            _pendingSignal = false;
            _running = true;
            int generation = _generation;
            FlushAsync(generation, _cancellation.Token);
        }

        private async void FlushAsync(int generation, CancellationToken cancellationToken)
        {
            LedgerFlushResult result = null;
            try
            {
                result = await _coordinator.FlushOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Disable/destroy cancellation is expected and keeps the outbox unchanged.
            }
            finally
            {
                if (generation == _generation)
                {
                    _running = false;
                    _nextAttemptAt = Time.unscaledTime + _intervalSeconds;
                    if (result != null &&
                        result.Status == LedgerFlushStatus.Uploaded &&
                        result.AcknowledgedCount > 0)
                    {
                        // Drain another player/batch on the next frame.
                        _pendingSignal = true;
                    }
                }
            }
        }

        private void OnEnable()
        {
            if (_coordinator != null && _cancellation == null)
            {
                _cancellation = new CancellationTokenSource();
                _pendingSignal = true;
            }
        }

        private void OnDisable()
        {
            _generation++;
            _running = false;
            if (_cancellation != null)
            {
                _cancellation.Cancel();
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        private void OnDestroy() => OnDisable();
    }
}
