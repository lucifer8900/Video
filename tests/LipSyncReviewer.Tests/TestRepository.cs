namespace LipSyncReviewer.Tests;

internal static class TestRepository
{
    public static DirectoryInfo FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Video.sln")))
            current = current.Parent;
        return current ?? throw new DirectoryNotFoundException("Could not locate the Video repository root.");
    }
}
