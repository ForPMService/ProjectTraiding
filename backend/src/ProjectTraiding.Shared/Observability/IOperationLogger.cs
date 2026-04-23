namespace ProjectTraiding.Shared.Observability;

public interface IOperationLogger
{
    ValueTask LogAsync(OperationEvent operationEvent, CancellationToken cancellationToken = default);
}
