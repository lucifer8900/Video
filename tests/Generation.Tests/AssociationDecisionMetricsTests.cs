using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Lingmai.RedMist.Generation.Associations;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class AssociationDecisionMetricsTests
{
    [Fact]
    public void RuntimeMetricsExposeBoundedTagsAndDeterministicPercentiles()
    {
        var measurements = new ConcurrentBag<ObservedMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AssociationDecisionMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new ObservedMeasurement(
                instrument.Name,
                value,
                tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new ObservedMeasurement(
                instrument.Name,
                value,
                tags.ToArray())));
        listener.Start();

        var metrics = new AssociationDecisionMetrics();
        for (int index = 1; index <= 100; index++)
        {
            bool fallback = index % 5 == 0;
            metrics.RecordCompleted(
                candidateCount: 1,
                fallbackUsed: fallback,
                elapsed: TimeSpan.FromMilliseconds(index),
                rejections: index == 1
                    ? new[]
                    {
                        new AssociationDecisionRejectionObservation(
                            "materialization",
                            "unapproved_text_variant")
                    }
                    : Array.Empty<AssociationDecisionRejectionObservation>());
        }

        AssociationDecisionMetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(100, snapshot.TotalDecisions);
        Assert.Equal(20, snapshot.FallbackDecisions);
        Assert.Equal(0.2d, snapshot.FallbackRate, 12);
        Assert.Equal(100, snapshot.CandidateCountDistribution[1]);
        Assert.Equal(1, snapshot.RejectionReasonDistribution["unapproved_text_variant"]);
        Assert.Equal(50d, snapshot.LatencyP50Milliseconds);
        Assert.Equal(95d, snapshot.LatencyP95Milliseconds);
        Assert.Equal(99d, snapshot.LatencyP99Milliseconds);

        Assert.Equal(100, measurements.Count(item =>
            item.Name == AssociationDecisionMetrics.CandidateCountInstrumentName));
        Assert.Equal(100, measurements.Count(item =>
            item.Name == AssociationDecisionMetrics.DecisionInstrumentName));
        Assert.Equal(100, measurements.Count(item =>
            item.Name == AssociationDecisionMetrics.DurationInstrumentName));
        ObservedMeasurement rejection = Assert.Single(measurements.Where(item =>
            item.Name == AssociationDecisionMetrics.RejectionInstrumentName));
        Assert.Contains(rejection.Tags, tag =>
            tag.Key == "reason" && Equals(tag.Value, "unapproved_text_variant"));
        Assert.Contains(rejection.Tags, tag =>
            tag.Key == "stage" && Equals(tag.Value, "materialization"));

        string[] allowedTags = { "outcome", "reason", "stage" };
        Assert.All(measurements, item => Assert.All(
            item.Tags,
            tag => Assert.Contains(tag.Key, allowedTags)));
        Assert.DoesNotContain(measurements.SelectMany(item => item.Tags), tag =>
            tag.Key.Contains("player", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("thread", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            tag.Key.Contains("chapter", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConcurrentRecordingDoesNotLoseDecisionOrFallbackCounts()
    {
        var metrics = new AssociationDecisionMetrics();

        await Task.WhenAll(Enumerable.Range(0, 500).Select(index => Task.Run(() =>
            metrics.RecordCompleted(
                index % 3,
                index % 2 == 0,
                TimeSpan.FromMilliseconds(index % 100),
                Array.Empty<AssociationDecisionRejectionObservation>()))));

        AssociationDecisionMetricsSnapshot snapshot = metrics.GetSnapshot();
        Assert.Equal(500, snapshot.TotalDecisions);
        Assert.Equal(250, snapshot.FallbackDecisions);
        Assert.Equal(0.5d, snapshot.FallbackRate, 12);
        Assert.Equal(500, snapshot.CandidateCountDistribution.Values.Sum());
    }

    private sealed record ObservedMeasurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags);
}
