namespace ProjectTraiding.Shared.Configuration;

public sealed class ObjectStorageOptions
{
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string BucketRaw { get; set; } = string.Empty;
    public string BucketExports { get; set; } = string.Empty;
}
