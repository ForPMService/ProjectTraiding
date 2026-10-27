using System.Text.Json;

namespace ProjectTraiding.Europe.News;

internal static class NewsJsonContractChecks
{
    internal static void Run()
    {
        const string acceptedWithoutFacts = """{"accepted":false,"facts":[]}""";
        const string acceptedWithGlobalEvent = """{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":"e582a772-6e14-4a91-9d5c-39871330d83a","new_global_event_name":null,"instruments":[]}]}""";
        const string acceptedWithFact = """{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":null,"new_global_event_name":null,"instruments":[]}] }""";
        NewsAgentExecutor.ValidateLlmResponse(acceptedWithoutFacts);
        NewsAgentExecutor.ValidateLlmResponse(acceptedWithFact);
        NewsAgentExecutor.ValidateLlmResponse(acceptedWithGlobalEvent);
        NewsLlmResponse parsedGlobalEvent = JsonSerializer.Deserialize(acceptedWithGlobalEvent,
            NewsAgentJsonContext.Default.NewsLlmResponse)
            ?? throw new InvalidOperationException("Ответ с UUID не прошёл десериализацию.");
        Guid globalEventId = Guid.Parse("e582a772-6e14-4a91-9d5c-39871330d83a");
        if (parsedGlobalEvent.Facts[0].GlobalEventId != globalEventId)
            throw new InvalidOperationException("Десериализация ответа потеряла UUID глобального сюжета.");
        string serializedGlobalEvent = JsonSerializer.Serialize(parsedGlobalEvent,
            NewsAgentJsonContext.Default.NewsLlmResponse);
        using (JsonDocument serializedDocument = JsonDocument.Parse(serializedGlobalEvent))
            if (serializedDocument.RootElement.GetProperty("facts")[0].GetProperty("global_event_id").GetString()
                != globalEventId.ToString())
                throw new InvalidOperationException("Сериализация ответа не представила UUID строкой.");

        ExpectRejected("""{"accepted":true,"facts":[]}""", "accepted=true с пустыми facts");
        ExpectRejected("""{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":"not-a-uuid","new_global_event_name":null,"instruments":[]}]}""",
            "невалидный UUID");
        ExpectRejected("""{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":"00000000-0000-0000-0000-000000000000","new_global_event_name":null,"instruments":[]}]}""",
            "пустой UUID существующего сюжета");
        ExpectRejected("""{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":"e582a772-6e14-4a91-9d5c-39871330d83a","new_global_event_name":"Another event","instruments":[]}]}""",
            "одновременно global_event_id и new_global_event_name");
        ExpectRejected("""{"accepted":false,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":null,"new_global_event_name":null,"instruments":[]}]}""",
            "accepted=false с фактами");
        ExpectRejected("""{"accepted":true,"facts":[{"event_type":"other","occurred_at":null,"global_event_id":null,"new_global_event_name":null,"instruments":[]}]}""",
            "отсутствующее обязательное поле");
        ExpectRejected("""{"accepted":false,"facts":[],"unexpected":true}""", "постороннее поле");
        ExpectRejected("""{"accepted":false,"accepted":false,"facts":[]}""", "повторяющееся поле");
        ExpectRejected("""{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":null,"new_global_event_name":null,"instruments":[{"secid":"SYNTH","instrument_relevance":1.01,"direction":null,"instrument_significance":null}]}]}""",
            "оценка вне диапазона");
        ExpectRejected("""{"accepted":true,"facts":[""", "повреждённый JSON");

        static void ExpectRejected(string json, string caseName)
        {
            try
            {
                NewsAgentExecutor.ValidateLlmResponse(json);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException)
            {
                return;
            }

            throw new InvalidOperationException($"Валидатор принял случай: {caseName}.");
        }
    }
}
