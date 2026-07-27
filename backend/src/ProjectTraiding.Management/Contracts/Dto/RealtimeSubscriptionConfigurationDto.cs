namespace ProjectTraiding.Management.Contracts.Dto
{
    public sealed record RealtimeSubscriptionConfigurationDto(
        string Secid,
        string[] DataKinds);
}
