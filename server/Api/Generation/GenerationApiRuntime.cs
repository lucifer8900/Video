namespace Lingmai.RedMist.Api.Generation;

public sealed class GenerationApiRuntimeOptions
{
    public const string SectionName = "Generation";

    public bool Enabled { get; init; }

    public static GenerationApiRuntimeOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        IConfigurationSection section = configuration.GetRequiredSection(SectionName);
        string? rawEnabled = section[nameof(Enabled)];
        if (!bool.TryParse(rawEnabled, out bool enabled))
            throw new InvalidOperationException("Generation:Enabled must be explicitly true or false.");

        var options = new GenerationApiRuntimeOptions { Enabled = enabled };
        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (Enabled)
        {
            throw new InvalidOperationException(
                "GENERATION_RUNTIME_INCOMPLETE: budget admission, PostgreSQL worker, " +
                "persistent media results, and delivery must be wired before generation can be enabled.");
        }
    }
}

public sealed class GenerationSubmissionGate
{
    public GenerationSubmissionGate(bool enabled) => Enabled = enabled;

    public bool Enabled { get; }
}
