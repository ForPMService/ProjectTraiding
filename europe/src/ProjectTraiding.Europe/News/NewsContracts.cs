using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.News;

/// <summary>Полный готовый снимок: справочники и настройки одного Realtime-разбора не меняются в ходе вызова.</summary>
public sealed record NewsConfigurationDTO(string Version, string PromptVersion, string Prompt,
    string Mode, ImmutableArray<string> EventTypes, ImmutableArray<NewsGlobalEventDTO> GlobalEvents,
    ImmutableArray<NewsInstrumentContextDTO> Instruments, JsonElement JsonSchema,
    string Model, int MaxOutputTokens, float Temperature, bool WebSearchEnabled)
{
    [JsonIgnore] internal string AgentVersion { get; init; } = "";
}

/// <summary>Существующий глобальный сюжет из справочника российского контура.</summary>
public sealed record NewsGlobalEventDTO(long Id, string Name);

/// <summary>Минимальный контекст инструмента по разделу 11 контракта v3; базовый актив может отсутствовать.</summary>
public sealed record NewsInstrumentContextDTO(string Secid, string Name, string InstrumentType, string? BaseAsset);

/// <summary>Техническая упаковка входа без повторения промпта и схемы, уже переданных параметрами запроса.</summary>
internal sealed record NewsAnalysisInputDTO(string Mode, ImmutableArray<string> EventTypes,
    ImmutableArray<NewsGlobalEventDTO> GlobalEvents, ImmutableArray<NewsInstrumentContextDTO> Instruments,
    TelegramChannelPost Publication);

/// <summary>Предметный ответ v3; отказ от полезных фактов отличается от технической ошибки.</summary>
public sealed record NewsAnalysisDTO(
    [property: JsonRequired] bool Accepted,
    [property: JsonRequired] ImmutableArray<NewsFactDTO> Facts);

/// <summary>Атомарный факт v3 с оценками непосредственно вложенных инструментов.</summary>
public sealed record NewsFactDTO(
    [property: JsonRequired] string EventType,
    [property: JsonRequired] string Summary,
    [property: JsonRequired] DateTimeOffset? OccurredAt,
    [property: JsonRequired] long? GlobalEventId,
    [property: JsonRequired] string? NewGlobalEventName,
    [property: JsonRequired] ImmutableArray<NewsInstrumentAssessmentDTO> Instruments);

/// <summary>Оценка экономической связи: аналитические значения имеют тип double, направление — целый тип.</summary>
public sealed record NewsInstrumentAssessmentDTO(
    [property: JsonRequired] string Secid,
    [property: JsonRequired] double InstrumentRelevance,
    [property: JsonRequired] int? Direction,
    [property: JsonRequired] double? InstrumentSignificance);

/// <summary>Внутренний выход следующему этапу без утверждения доставки или времени записи в российской базе.</summary>
public sealed record NewsAnalysisResultDTO(TelegramChannelPost Publication, string Json,
    string ConfigurationVersion, string Model, string AgentVersion);

/// <summary>Сериализация прикладных данных без рефлексии; все поля ответа обязательны, включая nullable.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false)]
[JsonSerializable(typeof(NewsConfigurationDTO))]
[JsonSerializable(typeof(NewsAnalysisInputDTO))]
[JsonSerializable(typeof(NewsAnalysisDTO))]
internal sealed partial class NewsJsonContext : JsonSerializerContext
{
    // Схема выводится только из полей и ограничений v3. Справочники окончательно проверяет будущий Writer.
    internal const string ContractSchema = """
        {
          "type":"object","additionalProperties":false,"required":["accepted","facts"],
          "properties":{
            "accepted":{"type":"boolean"},
            "facts":{"type":"array","items":{
              "type":"object","additionalProperties":false,
              "required":["event_type","summary","occurred_at","global_event_id","new_global_event_name","instruments"],
              "properties":{
                "event_type":{"type":"string"},"summary":{"type":"string"},
                "occurred_at":{"type":["string","null"],"format":"date-time"},
                "global_event_id":{"type":["integer","null"],"minimum":1},
                "new_global_event_name":{"type":["string","null"]},
                "instruments":{"type":"array","items":{
                  "type":"object","additionalProperties":false,
                  "required":["secid","instrument_relevance","direction","instrument_significance"],
                  "properties":{
                    "secid":{"type":"string"},
                    "instrument_relevance":{"type":"number","minimum":0,"maximum":1},
                    "direction":{"type":["integer","null"],"enum":[-1,0,1,null]},
                    "instrument_significance":{"type":["number","null"],"minimum":0,"maximum":1}
                  }
                }}
              }
            }}
          }
        }
        """;
}
