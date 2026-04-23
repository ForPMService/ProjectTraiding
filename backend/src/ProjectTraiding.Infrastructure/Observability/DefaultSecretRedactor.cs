using System;
using System.Collections.Generic;
using ProjectTraiding.Shared.Observability;

namespace ProjectTraiding.Infrastructure.Observability;

public sealed class DefaultSecretRedactor : ISecretRedactor
{
    private static readonly string[] SensitiveKeys = new[]
    {
        "password",
        "secret",
        "token",
        "apikey",
        "accesskey",
        "refreshtoken",
        "authorization"
    };

    public string? Redact(string? value)
    {
        if (value is null) return null;
        if (value.Length == 0) return string.Empty;

        var trimmed = value.Trim();

        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return "Bearer ***REDACTED***";
        }

        if (trimmed.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return "Basic ***REDACTED***";
        }

        // Conservative JWT-like detection: two dots and reasonably long
        var dotCount = 0;
        foreach (var c in trimmed)
        {
            if (c == '.') dotCount++;
            if (dotCount >= 2) break;
        }

        if (dotCount >= 2 && trimmed.Length > 20)
        {
            return "***REDACTED***";
        }

        return value;
    }

    public string? RedactByKey(string key, string? value)
    {
        if (value is null) return null;
        if (value.Length == 0) return string.Empty;

        if (IsSensitiveKey(key))
        {
            return "***REDACTED***";
        }

        return value;
    }

    private static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var k = key.ToLowerInvariant();

        foreach (var s in SensitiveKeys)
        {
            if (k.Contains(s, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
