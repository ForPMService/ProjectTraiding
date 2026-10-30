using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe;

/// <summary>Загружает профиль только выбранного провайдера; ключ не входит в снимок.</summary>
public sealed record LlmProfileOptions(string Name, string Provider, string Model, string? Endpoint,
    ReasoningEffort? ReasoningEffort, string? WebSearchContextSize = null, int? MaxToolCallCount = null)
{
    private const string DefaultXaiEndpoint = "https://api.x.ai/v1";

    public static LlmProfileOptions Load(IConfiguration configuration) =>
        LoadConfiguredActiveProfile(configuration, LoadCatalog(configuration));

    internal static ImmutableArray<LlmProfileOptions> LoadCatalog(IConfiguration configuration)
    {
        ImmutableArray<LlmProfileOptions>.Builder profiles = ImmutableArray.CreateBuilder<LlmProfileOptions>();
        AddProviderProfiles(configuration, "OpenAI", profiles);
        AddProviderProfiles(configuration, "XAI", profiles);
        return profiles.ToImmutable();
    }

    internal static LlmProfileOptions LoadConfiguredActiveProfile(IConfiguration configuration,
        ImmutableArray<LlmProfileOptions> profiles)
    {
        string? provider = NormalizeProvider(configuration["LLM:ActiveProvider"]);
        if (provider is null)
            throw new InvalidOperationException("LLM:ActiveProvider должен быть OpenAI или XAI.");

        string? name = configuration[$"{provider}:ActiveProfile"];
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Не задано имя активного профиля {provider}.");

        LlmProfileOptions? selected = null;
        foreach (LlmProfileOptions profile in profiles)
        {
            if (profile.Provider != provider || !string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (selected is not null)
                throw new InvalidOperationException($"Активный профиль {provider} неоднозначен.");
            selected = profile;
        }
        return selected ?? throw new InvalidOperationException($"Активный профиль {provider} отсутствует в каталоге.");
    }

    internal static string? NormalizeProvider(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "OPENAI" => "OpenAI",
        "XAI" => "XAI",
        _ => null
    };

    private static void AddProviderProfiles(IConfiguration configuration, string provider,
        ImmutableArray<LlmProfileOptions>.Builder profiles)
    {
        IConfigurationSection providerProfiles = configuration.GetSection($"{provider}:Profiles");
        string? endpoint = provider == "XAI" ? NormalizeXaiEndpoint(configuration["XAI:Endpoint"]) : null;
        foreach (IConfigurationSection profileSection in providerProfiles.GetChildren())
        {
            string? model = profileSection["Model"];
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException($"Профиль {provider}:{profileSection.Key} должен содержать модель.");

            ReasoningEffort? reasoningEffort = null;
            string? configuredReasoningEffort = profileSection["ReasoningEffort"];
            if (configuredReasoningEffort is not null)
            {
                string reasoningEffortName = configuredReasoningEffort.Trim();
                if (reasoningEffortName.Length == 0
                    || !Enum.TryParse(reasoningEffortName, true, out ReasoningEffort parsedReasoningEffort)
                    || !Enum.IsDefined(parsedReasoningEffort)
                    || !string.Equals(Enum.GetName(parsedReasoningEffort), reasoningEffortName,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Профиль {provider}:{profileSection.Key} содержит некорректный ReasoningEffort.");
                reasoningEffort = parsedReasoningEffort;
            }

            string? webSearchContextSize = null;
            string? configuredSearchContextSize = profileSection["WebSearchContextSize"];
            if (provider == "OpenAI" && configuredSearchContextSize is not null)
            {
                webSearchContextSize = configuredSearchContextSize.Trim().ToLowerInvariant();
                if (webSearchContextSize is not ("low" or "medium" or "high"))
                    throw new InvalidOperationException(
                        $"Профиль {provider}:{profileSection.Key} содержит некорректный WebSearchContextSize.");
            }

            int? maxToolCallCount = null;
            string? configuredMaxToolCallCount = profileSection["MaxToolCallCount"];
            if (provider == "OpenAI" && configuredMaxToolCallCount is not null)
            {
                if (!int.TryParse(configuredMaxToolCallCount, out int parsedMaxToolCallCount)
                    || parsedMaxToolCallCount < 1)
                    throw new InvalidOperationException(
                        $"Профиль {provider}:{profileSection.Key} содержит некорректный MaxToolCallCount.");
                maxToolCallCount = parsedMaxToolCallCount;
            }

            profiles.Add(new LlmProfileOptions(profileSection.Key, provider, model, endpoint, reasoningEffort,
                webSearchContextSize, maxToolCallCount));
        }
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

/// <summary>Секреты из одной принятой конфигурации; тип не сериализуется и не включается в версии агента.</summary>
internal sealed record LlmProviderCredentials(string? OpenAiApiKey, string? XaiApiKey)
{
    internal static LlmProviderCredentials Load(IConfiguration configuration) =>
        new(configuration["OpenAI:ApiKey"], configuration["XAI:ApiKey"]);

    internal string? GetApiKey(string provider) => provider switch
    {
        "OpenAI" => OpenAiApiKey,
        "XAI" => XaiApiKey,
        _ => null
    };
}
