using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ProjectTraiding.Europe;

/// <summary>Временная изолированная проверка выполнения Agents и Harness под Native AOT.</summary>
internal static class AgentAotProbe
{
    public static async Task<int> RunAsync()
    {
        const string expectedReply = "Локальный фиксированный ответ.";
        using IChatClient chatClient = new FixedReplyChatClient(expectedReply);

        ChatClientAgent chatClientAgent = new(chatClient, new ChatClientAgentOptions
        {
            Id = "aot-chat-client-probe",
            Name = "AOT ChatClientAgent probe",
            ChatOptions = new ChatOptions { ToolMode = ChatToolMode.None },
            UseProvidedChatClientAsIs = true
        });

        AgentResponse chatClientResponse = await chatClientAgent.RunAsync("Проверь ChatClientAgent.");
        Console.WriteLine($"ChatClientAgent: {chatClientResponse.Text}");

        HarnessAgent harnessAgent = chatClient.AsHarnessAgent(new HarnessAgentOptions
        {
            Id = "aot-harness-agent-probe",
            Name = "AOT HarnessAgent probe",
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableToolAutoApproval = true,
            DisableOpenTelemetry = true
        });

        AgentResponse harnessResponse = await harnessAgent.RunAsync("Проверь HarnessAgent.");
        Console.WriteLine($"HarnessAgent: {harnessResponse.Text}");

        bool chatClientSucceeded = chatClientResponse.Text == expectedReply;
        bool harnessSucceeded = harnessResponse.Text == expectedReply;
        Console.WriteLine($"ChatClientAgent result: {(chatClientSucceeded ? "PASS" : "FAIL")}");
        Console.WriteLine($"HarnessAgent result: {(harnessSucceeded ? "PASS" : "FAIL")}");
        return chatClientSucceeded && harnessSucceeded ? 0 : 1;
    }
}

/// <summary>Минимальная реализация IChatClient, возвращающая один фиксированный ответ без сетевого I/O.</summary>
internal sealed class FixedReplyChatClient(string reply) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield return new ChatResponseUpdate(ChatRole.Assistant, reply);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }
}
