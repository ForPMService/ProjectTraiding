using System.Net;
using System.Net.Http.Headers;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.Clients.Errors;

namespace TestHistoryData;

public class RedactionTests
{
    // ═══════════════════════════════════════════════════════════
    // 1. MoexAuthException не содержит секретов в Message/ToString
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void AuthException_DoesNotLeakSecrets()
    {
        var ex = new MoexAuthException("MOEX auth failure 401 for /test", "/test", 401);

        Assert.DoesNotContain("Bearer", ex.Message);
        Assert.DoesNotContain("Bearer", ex.ToString());
        Assert.DoesNotContain("Key", ex.ToString());
    }

    // ═══════════════════════════════════════════════════════════
    // 2. MoexRateLimitException — Message содержит только endpoint и статус
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void RateLimitException_MessageContainsOnlyEndpointAndStatus()
    {
        var ex = new MoexRateLimitException("MOEX rate limit 429 for /test", "/test", TimeSpan.FromSeconds(5));

        Assert.Contains("/test", ex.Message);
        Assert.Contains("429", ex.Message);
        Assert.True(ex.Message.Length < 200);
    }
}
