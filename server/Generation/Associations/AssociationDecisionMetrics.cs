using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lingmai.RedMist.Generation.Associations;

public sealed record AssociationDecisionRejectionObservation(
    string Stage,
    string Reason);

public sealed record AssociationDecisionMetricsSnapshot(
    long TotalDecisions,
    long FallbackDecisions,
    double FallbackRate,
    IReadOnlyDictionary<int, long> CandidateCountDistribution,
    IReadOnlyDictionary<string, long> RejectionReasonDistribution,
    double LatencyP50Milliseconds,
    double LatencyP95Milliseconds,
    double LatencyP99Milliseconds);

/// <summary>
/// Records bounded, low-cardinality association decision measurements while retaining a
/// deterministic in-process snapshot for health checks and tests. Backend monitoring computes
/// production percentiles from the duration histogram; the snapshot uses nearest-rank,
/// millisecond buckets and is intentionally independent for each service instance.
/// </summary>
public sealed class AssociationDecisionMetrics
{
    public const string MeterName = "Lingmai.RedMist.Associations";
    public const string CandidateCountInstrumentName = "lingmai.association.candidates";
    public const string DecisionInstrumentName = "lingmai.association.decisions";
    public const string DurationInstrumentName = "lingmai.association.duration";
    public const string RejectionInstrumentName = "lingmai.association.rejections";

    private const string IssuedOutcome = "issued";
    private const string FallbackOutcome = "fallback";
    private const string CandidateStage = "ranker";
    private const int MaximumSnapshotLatencyMilliseconds = 60_000;
    private const int MaximumRejectionsPerDecision = 1_024;

    private static readonly Meter RuntimeMeter = new(MeterName, "1.0.0");
    private static readonly Histogram<long> CandidateCountHistogram =
        RuntimeMeter.CreateHistogram<long>(
            CandidateCountInstrumentName,
            unit: "{candidate}",
            description: "Association candidates offered to the bounded ranker.");
    private static readonly Counter<long> DecisionCounter =
        RuntimeMeter.CreateCounter<long>(
            DecisionInstrumentName,
            unit: "{decision}",
            description: "Completed, audited association decisions by outcome.");
    private static readonly Histogram<double> DurationHistogram =
        RuntimeMeter.CreateHistogram<double>(
            DurationInstrumentName,
            unit: "ms",
            description: "End-to-end duration of completed, audited association decisions.");
    private static readonly Counter<long> RejectionCounter =
        RuntimeMeter.CreateCounter<long>(
            RejectionInstrumentName,
            unit: "{rejection}",
            description: "Bounded association rejection reasons by decision stage.");

    private readonly object _sync = new();
    private readonly Dictionary<int, long> _candidateCounts = new();
    private readonly Dictionary<string, long> _rejectionReasons =
        new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, long> _latencyMilliseconds = new();

    private long _totalDecisions;
    private long _fallbackDecisions;

    public void RecordCompleted(
        int candidateCount,
        bool fallbackUsed,
        TimeSpan elapsed,
        IReadOnlyList<AssociationDecisionRejectionObservation> rejections)
    {
        if (candidateCount < 0 ||
            candidateCount > AssociationRankerWireProtocol.MaximumProposalCount)
        {
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        }

        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        ArgumentNullException.ThrowIfNull(rejections);
        if (rejections.Count > MaximumRejectionsPerDecision)
            throw new ArgumentOutOfRangeException(nameof(rejections));

        AssociationDecisionRejectionObservation[] observations = rejections.ToArray();
        for (int index = 0; index < observations.Length; index++)
        {
            AssociationDecisionRejectionObservation? observation = observations[index];
            if (observation is null ||
                !IsMetricToken(observation.Stage) ||
                !IsMetricToken(observation.Reason))
            {
                throw new ArgumentException(
                    "Association rejection observations must use bounded metric tokens.",
                    nameof(rejections));
            }
        }

        int latencyBucket = ToLatencyBucket(elapsed);
        lock (_sync)
        {
            _totalDecisions = IncrementSaturating(_totalDecisions);
            if (fallbackUsed)
                _fallbackDecisions = IncrementSaturating(_fallbackDecisions);
            Increment(_candidateCounts, candidateCount);
            Increment(_latencyMilliseconds, latencyBucket);
            foreach (AssociationDecisionRejectionObservation observation in observations)
                Increment(_rejectionReasons, observation.Reason);
        }

        RecordRuntimeMeasurements(
            candidateCount,
            fallbackUsed,
            elapsed.TotalMilliseconds,
            observations);
    }

    public AssociationDecisionMetricsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            var candidateCounts = new ReadOnlyDictionary<int, long>(
                new SortedDictionary<int, long>(_candidateCounts));
            var rejectionReasons = new ReadOnlyDictionary<string, long>(
                new SortedDictionary<string, long>(
                    _rejectionReasons,
                    StringComparer.Ordinal));
            double fallbackRate = _totalDecisions == 0
                ? 0d
                : (double)_fallbackDecisions / _totalDecisions;

            return new AssociationDecisionMetricsSnapshot(
                _totalDecisions,
                _fallbackDecisions,
                fallbackRate,
                candidateCounts,
                rejectionReasons,
                Percentile(0.50d),
                Percentile(0.95d),
                Percentile(0.99d));
        }
    }

    private static void RecordRuntimeMeasurements(
        int candidateCount,
        bool fallbackUsed,
        double elapsedMilliseconds,
        IReadOnlyList<AssociationDecisionRejectionObservation> observations)
    {
        string outcome = fallbackUsed ? FallbackOutcome : IssuedOutcome;
        try
        {
            CandidateCountHistogram.Record(
                candidateCount,
                new KeyValuePair<string, object?>("stage", CandidateStage));
            DecisionCounter.Add(
                1,
                new KeyValuePair<string, object?>("outcome", outcome));
            DurationHistogram.Record(
                elapsedMilliseconds,
                new KeyValuePair<string, object?>("outcome", outcome));
            foreach (AssociationDecisionRejectionObservation observation in observations)
            {
                var tags = new TagList
                {
                    { "stage", observation.Stage },
                    { "reason", observation.Reason },
                };
                RejectionCounter.Add(1, tags);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Observability callbacks are external to the decision path. A faulty listener must
            // never turn an already audited decision into a gameplay failure.
        }
    }

    private double Percentile(double percentile)
    {
        if (_totalDecisions == 0) return 0d;
        long rank = checked((long)Math.Ceiling(_totalDecisions * percentile));
        long cumulative = 0;
        foreach ((int milliseconds, long count) in _latencyMilliseconds)
        {
            cumulative = AddSaturating(cumulative, count);
            if (cumulative >= rank) return milliseconds;
        }

        return _latencyMilliseconds.Count == 0
            ? 0d
            : _latencyMilliseconds.Last().Key;
    }

    private static int ToLatencyBucket(TimeSpan elapsed)
    {
        double milliseconds = elapsed.TotalMilliseconds;
        if (milliseconds >= MaximumSnapshotLatencyMilliseconds)
            return MaximumSnapshotLatencyMilliseconds;
        return Math.Max(
            0,
            checked((int)Math.Round(milliseconds, MidpointRounding.AwayFromZero)));
    }

    private static bool IsMetricToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64 ||
            value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            if ((character < 'a' || character > 'z') &&
                (character < '0' || character > '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static void Increment<TKey>(IDictionary<TKey, long> values, TKey key)
        where TKey : notnull
    {
        values.TryGetValue(key, out long current);
        values[key] = IncrementSaturating(current);
    }

    private static long IncrementSaturating(long value) =>
        value == long.MaxValue ? value : value + 1;

    private static long AddSaturating(long left, long right) =>
        long.MaxValue - left < right ? long.MaxValue : left + right;
}
