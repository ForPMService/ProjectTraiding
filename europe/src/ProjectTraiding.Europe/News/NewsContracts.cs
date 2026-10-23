using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using ProjectTraiding.Europe;
using ProjectTraiding.Europe.Telegram;

namespace ProjectTraiding.Europe.News;

/// <summary>Полный предметный снимок; активный профиль модели захватывается вместе с ним перед разбором.</summary>
public sealed record NewsAgentPromptSnapshot(string Version, string PromptVersion, string Prompt,
    string Mode, ImmutableArray<string> EventTypes, ImmutableArray<NewsGlobalEventDTO> GlobalEvents,
    ImmutableArray<NewsInstrumentContextDTO> Instruments, JsonElement JsonSchema,
    int MaxOutputTokens, float Temperature, bool WebSearchEnabled);

/// <summary>Один неизменяемый набор модели и предметного контекста, захваченный публикацией.</summary>
internal sealed record NewsAgentExecutionSnapshot(NewsAgentPromptSnapshot Subject,
    OpenAiProfileOptions Profile, ReasoningEffort? ReasoningEffort, string AgentVersion);

/// <summary>Только значимые настройки, вошедшие в agent_version; секреты сюда не передаются.</summary>
internal sealed record NewsAgentVersionMaterial(string Provider, string Model,
    ReasoningEffort? ReasoningEffort, NewsAgentPromptSnapshot Subject);

/// <summary>Существующий глобальный сюжет из справочника российского контура.</summary>
public sealed record NewsGlobalEventDTO(long Id, string Name);

/// <summary>Минимальный контекст инструмента по разделу 11 контракта v3; базовый актив может отсутствовать.</summary>
public sealed record NewsInstrumentContextDTO(string Secid, string Name, string InstrumentType, string? BaseAsset);

/// <summary>Техническая упаковка входа без повторения промпта и схемы, уже переданных параметрами запроса.</summary>
internal sealed record NewsLlmRequest(string Mode, ImmutableArray<string> EventTypes,
    ImmutableArray<NewsGlobalEventDTO> GlobalEvents, ImmutableArray<NewsInstrumentContextDTO> Instruments,
    TelegramChannelPost Publication);

/// <summary>Предметный ответ v3; отказ от полезных фактов отличается от технической ошибки.</summary>
public sealed record NewsLlmResponse(
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
public sealed record NewsAgentResult(TelegramChannelPost Publication, string Json,
    string ConfigurationVersion, string Model, string AgentVersion);

/// <summary>Сериализация прикладных данных без рефлексии; все поля ответа обязательны, включая nullable.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true, AllowDuplicateProperties = false)]
[JsonSerializable(typeof(NewsAgentPromptSnapshot))]
[JsonSerializable(typeof(NewsAgentVersionMaterial))]
[JsonSerializable(typeof(TelegramChannelPost))]
[JsonSerializable(typeof(NewsLlmRequest))]
[JsonSerializable(typeof(NewsLlmResponse))]
internal sealed partial class NewsAgentJsonContext : JsonSerializerContext
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
