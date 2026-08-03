namespace Lingmai.RedMist.MediaPipeline;

public sealed record ProcessSpec
{
    public ProcessSpec(
        string fileName,
        IReadOnlyList<string> argumentList,
        TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("An executable file name is required.", nameof(fileName));
        ArgumentNullException.ThrowIfNull(argumentList);
        if (argumentList.Any(argument => argument is null))
            throw new ArgumentException("Process arguments cannot contain null values.", nameof(argumentList));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        FileName = fileName;
        ArgumentList = argumentList.ToArray();
        Timeout = timeout;
    }

    public string FileName { get; }

    public IReadOnlyList<string> ArgumentList { get; }

    public TimeSpan Timeout { get; }
}

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string code, string message)
        : base(message) => Code = code;

    public string Code { get; }
}
