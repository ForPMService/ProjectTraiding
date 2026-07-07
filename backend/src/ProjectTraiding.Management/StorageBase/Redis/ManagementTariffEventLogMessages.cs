using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Redis
{
    /// <summary>
    /// Лог-события публикации изменения тарифов в поток оперативного хранилища.
    /// EventId 220–229: зарезервировано за management-tariff-event.
    /// </summary>
    public static partial class ManagementTariffEventLogMessages
    {
        [LoggerMessage(EventId = 220, EventName = "MgmtTariffEventPublished", Level = LogLevel.Information,
            Message = "Tariff change event published: stream={Stream}, id={Id}.")]
        public static partial void TariffEventPublished(ILogger logger, string stream, string id);

        [LoggerMessage(EventId = 221, EventName = "MgmtTariffEventPublishFailed", Level = LogLevel.Warning,
            Message = "Tariff change event publish failed: stream={Stream}, errorType={ErrorType}.")]
        public static partial void TariffEventPublishFailed(ILogger logger, Exception exception, string stream, string errorType);
    }
}
