using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineStockCardDto(
        string Secid,
        string Boardid,
        string? Shortname,
        string? Secname,
        string? Sectype,
        string? Isin,
        int? Lotsize,
        decimal? Minstep,
        int? Decimals,
        string? CurrencyId,
        long? IssueSize,
        int? ListLevel,
        string? Status);
}
