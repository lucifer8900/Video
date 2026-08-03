namespace Lingmai.RedMist.Api.Intent;

public static class StoryBundlePathResolver
{
    public static string Resolve(string configuredPath) => Resolve(
        configuredPath,
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory);

    public static string Resolve(
        string configuredPath,
        string workingDirectory,
        string applicationBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new ArgumentException("The story bundle path must not be empty.", nameof(configuredPath));
        if (string.IsNullOrWhiteSpace(workingDirectory))
            throw new ArgumentException("The working directory must not be empty.", nameof(workingDirectory));
        if (string.IsNullOrWhiteSpace(applicationBaseDirectory))
            throw new ArgumentException("The application base directory must not be empty.", nameof(applicationBaseDirectory));

        if (Path.IsPathRooted(configuredPath)) return Path.GetFullPath(configuredPath);

        string workingCandidate = Path.GetFullPath(configuredPath, workingDirectory);
        if (File.Exists(workingCandidate)) return workingCandidate;

        string applicationCandidate = Path.GetFullPath(configuredPath, applicationBaseDirectory);
        return File.Exists(applicationCandidate) ? applicationCandidate : workingCandidate;
    }
}
