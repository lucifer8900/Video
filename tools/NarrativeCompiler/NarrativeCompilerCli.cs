namespace NarrativeCompiler;

public static class NarrativeCompilerCli
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length > 0 &&
            string.Equals(args[0], "shot-manifest", StringComparison.Ordinal))
        {
            return RunShotManifest(args, stdout, stderr);
        }

        if (args.Length is < 1 or > 2 || string.IsNullOrWhiteSpace(args[0]))
        {
            stderr.WriteLine(
                "Usage: NarrativeCompiler <source-directory> [output-directory]");
            return 1;
        }

        try
        {
            var sourceDirectory = Path.GetFullPath(args[0]);
            var outputDirectory = args.Length == 2
                ? Path.GetFullPath(args[1])
                : sourceDirectory;

            var outputPath = new StoryBundleCompiler().Compile(
                sourceDirectory,
                outputDirectory);
            stdout.WriteLine(outputPath);
            return 0;
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"narrative-compiler: {exception.Message}");
            return 1;
        }
    }

    private static int RunShotManifest(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(args[1]))
        {
            stderr.WriteLine(
                "Usage: NarrativeCompiler shot-manifest <story.bundle.json> [output-directory]");
            return 1;
        }

        try
        {
            var bundlePath = Path.GetFullPath(args[1]);
            var outputDirectory = args.Length == 3
                ? Path.GetFullPath(args[2])
                : Path.GetDirectoryName(bundlePath)
                  ?? throw new DirectoryNotFoundException("Bundle directory is unavailable.");
            var outputPath = new ShotManifestGenerator().Generate(bundlePath, outputDirectory);
            stdout.WriteLine(outputPath);
            return 0;
        }
        catch (InvalidDataException exception)
        {
            stderr.WriteLine($"shot-manifest-generator: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            stderr.WriteLine($"shot-manifest-generator: {exception.Message}");
            return 1;
        }
    }
}
