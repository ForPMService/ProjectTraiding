using System.Text.Json;

namespace ProjectTraiding.Europe.News;

internal static class NewsJsonContractChecks
{
    internal static void Run()
    {
        const string acceptedWithoutFacts = """{"accepted":false,"facts":[]}""";
        const string acceptedWithFact = """{"accepted":true,"facts":[{"event_type":"other","summary":"synthetic","occurred_at":null,"global_event_id":null,"new_global_event_name":null,"instruments":[]}] }""";
        NewsAgentExecutor.ValidateLlmResponse(acceptedWithoutFacts);
        NewsAgentExecutor.ValidateLlmResponse(acceptedWithFact);

        ExpectRejected("""{"accepted":true,"facts":[]}""", "accepted=true с пустыми facts");
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
