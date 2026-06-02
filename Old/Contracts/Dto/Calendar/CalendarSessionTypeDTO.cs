namespace ProjectTraiding.Moex.Contracts.Dto.Calendar
{
    /// <summary>
    /// Справочник типов торговых сессий / временных слотов.
    ///
    /// Источник:
    /// ФР — GET /iss/calendars/stock/session.json
    /// СР — GET /iss/calendars/futures/session.json
    /// Таблица: session_schedule.types
    ///
    /// Одна строка — один тип.
    /// Структура одинаковая для stock и futures,
    /// но наборы типов различаются.
    ///
    /// Stock типы:
    /// oa_booking — Аукцион открытия - Период сбора заявок
    /// oa_pricing — Аукцион открытия - Начало фазы случайного определения цены
    /// system     — Системный режим торгов
    /// ca_booking — Аукцион закрытия - Период сбора заявок
    /// ca_pricing — Аукцион закрытия - Начало фазы случайного определения цены
    /// pd_booking — Аукцион размещения - Период сбора заявок
    /// pd_pricing — Аукцион размещения - Исполнение заявок
    ///
    /// Futures типы:
    /// oa_booking         — Аукцион открытия - период сбора заявок
    /// oa_pricing         — Аукцион открытия - фаза случайного определения цены
    /// morning_session    — Утренняя сессия
    /// main_session       — Основная сессия
    /// evening_session    — Вечерняя сессия
    /// weekend_session    — Дополнительная сессия выходного дня
    /// settlement_session — Начало расчетной сессии
    /// clearing_session   — Начало клиринговой сессии
    /// </summary>
    public record CalendarSessionTypeDTO
    {
        /// <summary>
        /// Код типа сессии / слота.
        ///
        /// MOEX столбец: type
        /// MOEX type: string
        ///
        /// Пример: oa_booking, main_session.
        /// </summary>
        public string? Type { get; init; }

        /// <summary>
        /// Человекочитаемое название типа.
        ///
        /// MOEX столбец: title
        /// MOEX type: string
        ///
        /// Пример: "Основная сессия".
        /// </summary>
        public string? Title { get; init; }
    }
}