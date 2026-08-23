using System.Buffers;
using System.Text;
using System.Text.Json;

namespace ProjectTraiding.Diagnostics.Endpoints
{
    /// <summary>
    /// Общие вспомогательные операции диагностических маршрутов. Ответы строятся
    /// стандартным JSON-писателем, поэтому значения экранируются теми же правилами,
    /// что и в остальных JSON-ответах приложения.
    /// </summary>
    internal static class DiagnosticResponses
    {
        internal static IResult CreateDiagnosticError(
            int statusCode,
            string source,
            string kind,
            string market,
            string message,
            string? ticker = null)
        {
            return Results.Text(
                SerializeError(source, kind, market, message, ticker),
                "application/json",
                statusCode: statusCode);
        }

        internal static Dictionary<string, string> CollectPassThroughQuery(
            HttpRequest request,
            bool excludeSource = false)
        {
            Dictionary<string, string> queryParams = new();
            foreach (string key in request.Query.Keys)
            {
                if (excludeSource && string.Equals(key, "source", StringComparison.OrdinalIgnoreCase))
                    continue;

                queryParams[key] = request.Query[key].ToString();
            }

            return queryParams;
        }

        internal static bool IsSafePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            foreach (char symbol in value)
            {
                if (!char.IsLetterOrDigit(symbol))
                    return false;
            }

            return true;
        }

        private static string SerializeError(
            string source,
            string kind,
            string market,
            string message,
            string? ticker)
        {
            ArrayBufferWriter<byte> buffer = new();
            using Utf8JsonWriter writer = new(buffer);
            writer.WriteStartObject();
            writer.WriteString("status", "error");
            writer.WriteString("source", source);
            writer.WriteString("kind", kind);
            writer.WriteString("market", market);
            if (ticker is not null)
                writer.WriteString("ticker", ticker);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.Flush();

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
