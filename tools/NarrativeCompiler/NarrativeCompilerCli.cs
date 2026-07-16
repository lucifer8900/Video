namespace NarrativeCompiler;

public static class NarrativeCompilerCli
{
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

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
}
