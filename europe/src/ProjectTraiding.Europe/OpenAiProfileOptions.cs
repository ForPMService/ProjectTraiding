using Microsoft.Extensions.Configuration;

namespace ProjectTraiding.Europe;

/// <summary>Выбирает единственный доступный сейчас провайдер из именованного профиля конфигурации.</summary>
public sealed record OpenAiProfileOptions(string Name, string Provider, string Model)
{
    public static OpenAiProfileOptions Load(IConfiguration configuration)
    {
        string? name = configuration["OpenAI:ActiveProfile"];
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Не задано имя активного профиля OpenAI.");

        IConfigurationSection profile = configuration.GetSection("OpenAI:Profiles").GetSection(name);
        string? provider = profile["Provider"];
        string? model = profile["Model"];
        if (provider is null || !string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Активный профиль OpenAI должен содержать провайдера OpenAI и модель.");

        return new OpenAiProfileOptions(name, provider, model);
    }
}
