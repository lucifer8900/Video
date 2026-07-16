using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Production clock for generation orchestration. Deadlines use Unity's monotonic realtime
    /// clock, while signed-ticket expiry uses UTC wall-clock time.
    /// </summary>
    public sealed class UnityGenerationFlowClock : IGenerationFlowClock
    {
        public double MonotonicSeconds => Time.realtimeSinceStartupAsDouble;

        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
            return Task.Delay(delay, cancellationToken);
        }
    }
}
