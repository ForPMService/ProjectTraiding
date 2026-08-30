namespace ProjectTraiding.CustomFeatures.Contracts
{
    public readonly record struct InstrumentRelationUpsertCommand(
        string SourceSecid,
        string RelationType,
        string Confidence,
        string? TargetSecid,
        string? TargetAssetCode,
        string? Comment);
}
