using System.Text.Json.Serialization;

namespace ProjectTraiding.Api.Telegram;

public sealed class TelegramAuthAnswerRequest
{
    public string? ExpectedField { get; init; }
    public string? Value { get; init; }
}

public sealed record TelegramAuthStatusResponse(string State, string? ExpectedField);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TelegramAuthAnswerRequest))]
[JsonSerializable(typeof(TelegramAuthStatusResponse))]
public partial class TelegramAuthJsonContext : JsonSerializerContext;
