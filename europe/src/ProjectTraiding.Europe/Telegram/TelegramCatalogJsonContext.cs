using System.Text.Json.Serialization;

namespace ProjectTraiding.Europe.Telegram;

/// <summary>Versioned catalog; contains normalized public names only.</summary>
internal sealed class TelegramCatalogDocument
{
    public int Version { get; init; }
    public string[]? Channels { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(TelegramCatalogDocument))]
[JsonSerializable(typeof(string[]))]
internal partial class TelegramCatalogJsonContext : JsonSerializerContext;
