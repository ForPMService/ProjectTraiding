using Microsoft.AspNetCore.Http;

namespace ProjectTraiding.Api.Observability;

public interface ICorrelationIdAccessor
{
    string? GetCorrelationId();
}

public class HttpContextCorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private const string ItemKey = "CorrelationId";

    public HttpContextCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCorrelationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        if (context.Items.TryGetValue(ItemKey, out var value) && value is string s)
        {
            return s;
        }

        return null;
    }
}
