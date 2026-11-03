namespace ProjectTraiding.CustomFeatures.Messaging;

public sealed class CustomFeaturesWolverineSmokeAcknowledgement
{
    private readonly TaskCompletionSource<Guid> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<Guid> WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        return _completion.Task.WaitAsync(cancellationToken);
    }

    public void Confirm(Guid messageId)
    {
        _completion.TrySetResult(messageId);
    }
}
