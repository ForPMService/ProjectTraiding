using ProjectTraiding.Moex.Errors;
using ProjectTraiding.Moex.Infrastructure.Telemetry;

namespace ProjectTraiding.Moex.Parsing.Errors
{
    /// <summary>
    /// Ошибка несовпадения ожидаемой схемы MOEX с фактическим ответом источника.
    /// Выбрасывается, когда обязательные колонки не найдены в блоке columns[].
    /// </summary>
    public sealed class MoexSchemaMismatchException : MoexException
    {
        public override string ErrorCategory => MoexErrorTypes.SchemaMismatch;

        public string? Endpoint { get; }

        public MoexSchemaMismatchException(
            string message,
            string? endpoint = null)
            : base(message)
        {
            Endpoint = endpoint;
        }
    }
}
