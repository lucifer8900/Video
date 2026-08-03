using Lingmai.RedMist.Api.Intent;

namespace Lingmai.RedMist.Api.Tests;

public sealed class InvalidInputDetectorTests
{
    private readonly InvalidInputDetector _detector = new();

    [Fact]
    public void WhitespaceIsSilenceAndNeverNeedsAClassifier()
    {
        InvalidInputSignal? signal = _detector.Detect(" \u3000\t", 512);

        Assert.NotNull(signal);
        Assert.Equal(InvalidInputKind.Silence, signal.Kind);
        Assert.Equal(1d, signal.Confidence);
    }

    [Fact]
    public void StoryLimitPlusOneIsTooLongEvenWhenItAlsoContainsAbuse()
    {
        string transcript = "滚开" + new string('甲', 511);

        InvalidInputSignal? signal = _detector.Detect(transcript, 512);

        Assert.NotNull(signal);
        Assert.Equal(InvalidInputKind.TooLong, signal.Kind);
    }

    [Theory]
    [InlineData("滚开")]
    [InlineData("你这个蠢货")]
    [InlineData("ｇｕｎ开，废物")]
    public void ExplicitAbuseIsDetectedAfterFormKcNormalization(string transcript)
    {
        InvalidInputSignal? signal = _detector.Detect(transcript, 512);

        Assert.NotNull(signal);
        Assert.Equal(InvalidInputKind.Abuse, signal.Kind);
        Assert.InRange(signal.Confidence, 0d, 1d);
    }

    [Theory]
    [InlineData("滚水已经烧开")]
    [InlineData("把废弃法器收起来")]
    [InlineData("我想先观察阵眼")]
    public void OrdinaryTextIsNotMisclassifiedAsAbuse(string transcript)
    {
        Assert.Null(_detector.Detect(transcript, 512));
    }
}
