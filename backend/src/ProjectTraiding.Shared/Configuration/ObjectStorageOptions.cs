namespace ProjectTraiding.Shared.Configuration;

public sealed class ObjectStorageOptions
{
    public string Provider { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string BucketRaw { get; init; } = string.Empty;
    public string BucketExports { get; init; } = string.Empty;
}
