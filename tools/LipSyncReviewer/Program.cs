using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await LipSyncReviewer.LipSyncReviewerCli.RunAsync(
    args,
    Console.Out,
    Console.Error,
    TimeProvider.System,
    cancellation.Token);
