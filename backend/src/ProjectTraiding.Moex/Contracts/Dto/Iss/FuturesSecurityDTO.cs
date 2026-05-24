namespace ProjectTraiding.Moex.Contracts.Dto.Iss
{
    /// <summary>
    /// Информация о фьючерсной ценной бумаге.
    /// </summary>
    public record FuturesSecurityDTO
    {
        public string? SECID { get; init; } // тикер(SiM6, BRN5 и т.д.)
        public string? SHORTNAME { get; init; } // короткое название
        public string? SECNAME { get; init; } // полное название
        public string? ASSETCODE { get; init; } // базовый актив(Si, BR, RI...)
        public double? INITIALMARGIN { get; init; } // ГО
        public double? PREVSETTLEPRICE { get; init; } //      расчётная цена закрытия вчера
        public double? PREVPRICE { get; init; } // цена последней сделки вчера
        public double? MINSTEP { get; init; } // минимальный шаг цены
        public double? STEPPRICE { get; init; } // стоимость шага в рублях
        public int? LOTVOLUME { get; init; } // размер лота
        public DateTime? LASTTRADEDATE { get; init; } // последний день торгов
        public DateTime? LASTDELDATE { get; init; } // дата экспирации
        public long? PREVOPENPOSITION { get; init; } // открытый интерес вчера
        public double? HIGHLIMIT { get; init; } // верхний лимит цены
        public double? LOWLIMIT { get; init; } // нижний лимит цены
        public int? DECIMALS { get; init; } // количество знаков после запятой
    }
}