namespace ProjectTraiding.Shared.Observability;

public interface ISecretRedactor
{
    string? Redact(string? value);

    string? RedactByKey(string key, string? value);
}
