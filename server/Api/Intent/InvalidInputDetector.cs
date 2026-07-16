using System.Text;

namespace Lingmai.RedMist.Api.Intent;

public sealed class InvalidInputDetector
{
    private static readonly string[] AbusePhrases =
    [
        "滚开",
        "蠢货",
        "废物",
        "去死",
        "闭嘴",
    ];

    public InvalidInputSignal? Detect(string? transcript, int storyCharacterLimit)
    {
        if (storyCharacterLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(storyCharacterLimit));

        string normalized = (transcript ?? string.Empty)
            .Normalize(NormalizationForm.FormKC)
            .Trim();
        if (normalized.Length == 0)
            return new InvalidInputSignal(InvalidInputKind.Silence, 1d, "detector");
        if (normalized.Length > storyCharacterLimit)
            return new InvalidInputSignal(InvalidInputKind.TooLong, 1d, "detector");

        foreach (string phrase in AbusePhrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
                return new InvalidInputSignal(InvalidInputKind.Abuse, 0.98d, "detector");
        }

        return null;
    }
}
