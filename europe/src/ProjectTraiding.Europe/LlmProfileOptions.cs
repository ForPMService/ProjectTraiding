using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe;

/// <summary>Загружает поддержанный профиль только выбранного провайдера; ключ не входит в снимок.</summary>
public sealed record LlmProfileOptions(string Name, string Provider, string Model, string? Endpoint,
    ReasoningEffort? ReasoningEffort)
{
    private const string DefaultXaiEndpoint = "https://api.x.ai/v1";

    public static LlmProfileOptions Load(IConfiguration configuration)
    {
        string? activeProvider = configuration["LLM:ActiveProvider"];
        string provider = activeProvider?.Trim().ToUpperInvariant() switch
        {
            "OPENAI" => "OpenAI",
            "XAI" => "XAI",
            _ => throw new InvalidOperationException("LLM:ActiveProvider должен быть OpenAI или XAI.")
        };

        string? name = configuration[$"{provider}:ActiveProfile"];
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Не задано имя активного профиля {provider}.");

        IConfigurationSection profile = configuration.GetSection($"{provider}:Profiles").GetSection(name);
        string? model = profile["Model"];
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException($"Активный профиль {provider} должен содержать модель.");

        if (provider == "OpenAI")
        {
            ReasoningEffort? reasoningEffort = model switch
            {
                "gpt-4.1-2025-04-14" => null,
                "gpt-6-luna" => Microsoft.Extensions.AI.ReasoningEffort.None,
                _ => throw new InvalidOperationException("Модель профиля OpenAI не подтверждена для News.")
            };
            return new LlmProfileOptions(name, provider, model, null, reasoningEffort);
        }

        string endpoint = NormalizeXaiEndpoint(configuration["XAI:Endpoint"]);
        ReasoningEffort xaiReasoningEffort = model switch
        {
            "grok-4.7" or "grok-4.6" => Microsoft.Extensions.AI.ReasoningEffort.High,
            _ => throw new InvalidOperationException("Модель профиля XAI не подтверждена для News Structured Outputs.")
        };
        return new LlmProfileOptions(name, provider, model, endpoint, xaiReasoningEffort);
    }

    internal static string GetApiKey(IConfiguration configuration, LlmProfileOptions profile)
    {
        string? apiKey = configuration[$"{profile.Provider}:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"Отсутствует {profile.Provider}:ApiKey в конфигурации.");
        return apiKey;
    }

    internal static string GetXaiEndpoint(IConfiguration configuration) =>
        NormalizeXaiEndpoint(configuration["XAI:Endpoint"]);

    private static string NormalizeXaiEndpoint(string? configuredEndpoint)
    {
        string endpoint = string.IsNullOrWhiteSpace(configuredEndpoint) ? DefaultXaiEndpoint : configuredEndpoint;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.AbsolutePath.TrimEnd('/') != "/v1"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.Host is not ("api.x.ai" or "us.api.x.ai"))
            throw new InvalidOperationException("XAI:Endpoint должен быть https://api.x.ai/v1 или https://us.api.x.ai/v1.");

        return $"{uri.GetLeftPart(UriPartial.Authority)}/v1";
    }
}
