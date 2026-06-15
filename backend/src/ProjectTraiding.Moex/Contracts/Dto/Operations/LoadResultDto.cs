namespace ProjectTraiding.Moex.Contracts.Dto.Operations
{
    public sealed record LoadResultDto(
        string Operation,
        string Source,
        string Target,
        string Status,
        int InputCount,
        int RowsWritten,
        double ElapsedMs);
}