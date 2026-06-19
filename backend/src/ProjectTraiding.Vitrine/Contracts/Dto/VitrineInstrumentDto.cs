using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Contracts.Dto
{
    public sealed record VitrineInstrumentDto(
        string Secid,
        string InstrumentType,         // stock | futures
        string? AssetCode,             // только у фьючерсов
        string Shortname,
        string Secname);
}
