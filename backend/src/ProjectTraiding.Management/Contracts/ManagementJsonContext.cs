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
    public partial class ManagementJsonContext : JsonSerializerContext;
    
}
