using System.Text.Json;

namespace StoryValidator;

public static class StoryValidatorCli
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length != 1)
        {
            stderr.WriteLine("Usage: StoryValidator <story.bundle.json | directory>");
            return 1;
        }

        try
        {
            var inputPath = Path.GetFullPath(args[0]);
            var bundlePath = Directory.Exists(inputPath)
                ? Path.Combine(inputPath, "story.bundle.json")
                : inputPath;

            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException("The story bundle was not found.", bundlePath);
            }

            var schemaDirectory = Path.Combine(AppContext.BaseDirectory, "schemas");
            if (!Directory.Exists(schemaDirectory))
            {
                throw new DirectoryNotFoundException(
                    $"The schema directory was not found: {schemaDirectory}");
            }

            var result = new StoryBundleValidator().Validate(bundlePath, schemaDirectory);
            stdout.WriteLine(JsonSerializer.Serialize(result, OutputOptions));

            return result.Valid ? 0 : 2;
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"story-validator: {exception.Message}");
            return 1;
        }
    }
}
