namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    public record CalendarFortsContractDTO
    {
        public string? SecId { get; init; }
        public string? AssetCode { get; init; }
        public string? ShortName { get; init; }
        public string? ExecType { get; init; }
        public string? ContractName { get; init; }
        public string? ExpirationDate { get; init; }
        public string? EndDate { get; init; }
        public string? ExpirationType { get; init; }
        public string? ExpirationTime { get; init; }
        public int? WeekendSession { get; init; }
    }

}
