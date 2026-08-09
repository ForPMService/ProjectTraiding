namespace ProjectTraiding.Management.Contracts.Dto
{
    /// <summary>
    /// Итог команды отмены заданий, общий для обоих масштабов.
    /// Scope принимает значения "all" и "instrument"; Secid заполняется только
    /// для масштаба instrument, для all остаётся null.
    ///
    /// CancelRequestedCount — число выполняющихся заданий, впервые помеченных именно
    /// этим вызовом, а не снимок всех выполняющихся заданий.
    ///
    /// Числа всех работающих заданий здесь намеренно нет. Согласованно посчитать его
    /// нельзя: конкурентный захват дорожкой исполнителя может закоммититься уже
    /// после снимка нашего оператора, и возвращённое значение оказалось бы занижено.
    /// Текущее число работающих заданий наблюдается метрикой moex_load_tasks_active
    /// на панели moex-data-loads — непрерывный ряд для этого вопроса лучше
    /// мгновенного снимка в ответе команды.
    /// </summary>
    public sealed record LoadTasksCancelResponse(
        string Scope,
        string? Secid,
        int CancelledCount,
        int CancelRequestedCount,
        double ElapsedMs);
}
