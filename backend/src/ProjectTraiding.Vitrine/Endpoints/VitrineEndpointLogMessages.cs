using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.Endpoints
{
    /// <summary>
    /// Лог-события endpoint-контура Vitrine.
    /// EventId 310–319: зарезервировано за vitrine-endpoint.
    /// </summary>
    public static partial class VitrineEndpointLogMessages
    {
        [LoggerMessage(
        EventId = 310, EventName = "VitrineOperationStarted", Level = LogLevel.Information,
        Message = "Vitrine read operation started: route={Route}.")]
        public static partial void OperationStarted(ILogger logger, string route);

    }
}
