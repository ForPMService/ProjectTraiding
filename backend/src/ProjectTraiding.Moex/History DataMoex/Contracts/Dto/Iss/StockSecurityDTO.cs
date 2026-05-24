namespace History_DataMoex.Contracts.Dto.Iss
{
    /// <summary>
    /// Информация о ценной бумаге.
    /// </summary>
    public record StockSecurityDTO
    {
        public string? SECID { get; init; }
        public string? SHORTNAME { get; init; }
        public string? SECNAME { get; init; }
        public string? BOARDID { get; init; }
        public decimal? PREVLEGALCLOSEPRICE { get; init; }
        public int? LOTSIZE { get; init; }
        public double? FACEVALUE { get; init; }
        public string? MARKETCODE { get; init; }
        public DateTime? PREVDATE { get; init; }
    }
}