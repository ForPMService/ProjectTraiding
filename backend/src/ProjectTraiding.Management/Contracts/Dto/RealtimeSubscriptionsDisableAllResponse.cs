namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>
    /// Итог массового выключения подписок приёма. Отдельный тип, а не переиспользование
    /// RealtimeSubscriptionResultDto: у операции нет ни кода инструмента, ни вида данных,
    /// и подставлять в эти поля условное значение означало бы врать в контракте.
    /// </summary>
    public sealed record RealtimeSubscriptionsDisableAllResponse(
        string Operation,
        int DisabledCount,
        double ElapsedMs);
}
