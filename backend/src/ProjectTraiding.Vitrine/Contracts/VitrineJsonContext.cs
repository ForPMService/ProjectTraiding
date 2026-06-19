using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Vitrine.Contracts
{
    [JsonSerializable(typeof(VitrineInstrumentDto))]
    [JsonSerializable(typeof(List<VitrineInstrumentDto>))]
    [JsonSerializable(typeof(VitrineCalendarDayDto))]
    [JsonSerializable(typeof(List<VitrineCalendarDayDto>))]
    [JsonSerializable(typeof(VitrineBrokerTariffDto))]
    [JsonSerializable(typeof(List<VitrineBrokerTariffDto>))]
    [JsonSerializable(typeof(VitrineInstrumentRelationDto))]
    [JsonSerializable(typeof(List<VitrineInstrumentRelationDto>))]
    [JsonSerializable(typeof(VitrineStockCardDto))]
    [JsonSerializable(typeof(List<VitrineStockCardDto>))]
    [JsonSerializable(typeof(VitrineFuturesCardDto))]
    [JsonSerializable(typeof(List<VitrineFuturesCardDto>))]
    [JsonSerializable(typeof(VitrineStatusDto))]
    public partial class VitrineJsonContext : JsonSerializerContext;
}
