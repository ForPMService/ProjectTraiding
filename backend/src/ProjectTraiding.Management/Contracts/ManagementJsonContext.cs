using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace ProjectTraiding.Management.Contracts
{
    [JsonSerializable(typeof(BrokerTariffCreateRequest))]
    [JsonSerializable(typeof(InstrumentRelationCreateRequest))]
    [JsonSerializable(typeof(ManagementResultDto))]
    [JsonSerializable(typeof(LoadTaskCreateRequest))]
    [JsonSerializable(typeof(LoadTaskBulkCreateRequest))]
    [JsonSerializable(typeof(LoadTaskBulkCreateResponse))]
    [JsonSerializable(typeof(RealtimeSubscriptionResultDto))]
    [JsonSerializable(typeof(RealtimeSubscriptionsDisableAllResponse))]
    [JsonSerializable(typeof(LoadTasksCancelResponse))]
    [JsonSerializable(typeof(InstrumentDataDeleteResponse))]
    [JsonSerializable(typeof(CalendarLoadRequest))]
    [JsonSerializable(typeof(CalendarDayOverrideRequest))]
    [JsonSerializable(typeof(CalendarOperationResponse))]
    [JsonSerializable(typeof(ManualEventCreateRequest))]
    [JsonSerializable(typeof(ManualEventCreateResponse))]
    [JsonSerializable(typeof(TradingPeriodCreateRequest))]
    [JsonSerializable(typeof(TradingPeriodTypeCreateRequest))]
    [JsonSerializable(typeof(LoadResultDto))]
    [JsonSerializable(typeof(LoadResultDto[]))]
    public partial class ManagementJsonContext : JsonSerializerContext;
    
}
