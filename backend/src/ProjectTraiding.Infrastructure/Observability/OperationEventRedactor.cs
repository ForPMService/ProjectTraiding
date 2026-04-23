using System;
using System.Collections.Generic;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

internal sealed class OperationEventRedactor
{
    private readonly ISecretRedactor _secretRedactor;

    public OperationEventRedactor(ISecretRedactor secretRedactor)
    {
        _secretRedactor = secretRedactor ?? throw new ArgumentNullException(nameof(secretRedactor));
    }

    public OperationEvent Redact(OperationEvent operationEvent)
    {
        if (operationEvent is null) return operationEvent;

        var message = _secretRedactor.Redact(operationEvent.Message) ?? string.Empty;

        IReadOnlyDictionary<string, string>? details = null;
        if (operationEvent.Details != null)
        {
            var dict = new Dictionary<string, string>(operationEvent.Details.Count);
            foreach (var kvp in operationEvent.Details)
            {
                var byKey = _secretRedactor.RedactByKey(kvp.Key, kvp.Value);
                var intermediate = byKey ?? kvp.Value;
                var final = _secretRedactor.Redact(intermediate);
                dict[kvp.Key] = final ?? intermediate ?? string.Empty;
            }

            details = dict;
        }

        return operationEvent with { Message = message, Details = details };
    }
}
