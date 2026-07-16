namespace Lingmai.RedMist.Api.Intent;

public sealed class IntentOptions
{
    public string Provider { get; set; } = "Mock";
    public string StoryBundlePath { get; set; } = "content/story/red-mist/story.bundle.json";
    public int MaxTranscriptCharacters { get; set; } = 512;
    public int MaxAcceptedTranscriptCharacters { get; set; } = 4096;
    public string MockOutcome { get; set; } = "irrelevant";
    public string? MockIntentId { get; set; }
    public double MockConfidence { get; set; }
    public OpenAiIntentOptions OpenAi { get; set; } = new();
}

public sealed class OpenAiIntentOptions
{
    public Uri Endpoint { get; set; } = new("https://api.openai.com/v1/responses");
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 15;
}
