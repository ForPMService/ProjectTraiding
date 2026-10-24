using System.Text.Json.Serialization;

namespace ProjectTraiding.Europe;

internal sealed record LlmModelDTO(string Provider, string Model, bool IsActive);

internal sealed record LlmActiveModelRequestDTO(
    [property: JsonRequired] string? Provider,
    [property: JsonRequired] string? Model);

internal sealed record LlmManagementErrorDTO(string Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(LlmModelDTO[]))]
[JsonSerializable(typeof(LlmActiveModelRequestDTO))]
[JsonSerializable(typeof(LlmManagementErrorDTO))]
internal sealed partial class LlmManagementHttpJsonContext : JsonSerializerContext
{
}
