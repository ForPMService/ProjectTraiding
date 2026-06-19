using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineFuturesCardDto(
        string Secid,
        string Boardid,
        string? Shortname,
        string? Secname,
        string? AssetCode,
        decimal? InitialMargin,
        decimal? Minstep,
        decimal? Stepprice,
        int? Lotvolume,
        int? Decimals,
        DateOnly? LastTradeDate,       // экспирация
        DateOnly? LastDelDate,         // дата поставки
        decimal? HighLimit,
        decimal? LowLimit,
        decimal? BuysellFee);          // комиссия биржи
}
