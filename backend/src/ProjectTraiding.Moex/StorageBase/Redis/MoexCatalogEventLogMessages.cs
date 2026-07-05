using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Лог-события публикации изменения справочника в поток оперативного хранилища.
    /// EventId 400–409: зарезервировано за moex-catalog-event.
    /// Если этот диапазон уже занят другим классом контура биржи — сдвиньте номера,
    /// они не должны пересекаться в пределах контура.
    /// </summary>
    public static partial class MoexCatalogEventLogMessages
    {
        [LoggerMessage(EventId = 400, EventName = "MoexCatalogEventPublished", Level = LogLevel.Information,
            Message = "Catalog change event published: stream={Stream}, id={Id}.")]
        public static partial void CatalogEventPublished(ILogger logger, string stream, string id);

        [LoggerMessage(EventId = 401, EventName = "MoexCatalogEventPublishFailed", Level = LogLevel.Warning,
            Message = "Catalog change event publish failed: stream={Stream}, errorType={ErrorType}.")]
        public static partial void CatalogEventPublishFailed(ILogger logger, Exception exception, string stream, string errorType);
    }
}
