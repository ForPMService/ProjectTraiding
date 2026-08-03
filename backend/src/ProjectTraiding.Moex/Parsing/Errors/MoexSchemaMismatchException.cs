using System.Collections.ObjectModel;
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

        public string? SourceCode { get; }

        public string? DataNeedCode { get; }

        public string? Endpoint { get; }

        public IReadOnlyList<string> ExpectedColumns { get; }

        public IReadOnlyList<string> ActualColumns { get; }

        public IReadOnlyList<string> MissingColumns { get; }

        public Guid? RawObjectId { get; }

        public MoexSchemaMismatchException(
            string message,
            IReadOnlyList<string> expectedColumns,
            IReadOnlyList<string> actualColumns,
            IReadOnlyList<string> missingColumns,
            string? sourceCode = null,
            string? dataNeedCode = null,
            string? endpoint = null,
            Guid? rawObjectId = null)
            : base(message)
        {
            SourceCode = sourceCode;
            DataNeedCode = dataNeedCode;
            Endpoint = endpoint;
            ExpectedColumns = new ReadOnlyCollection<string>(expectedColumns.ToArray());
            ActualColumns = new ReadOnlyCollection<string>(actualColumns.ToArray());
            MissingColumns = new ReadOnlyCollection<string>(missingColumns.ToArray());
            RawObjectId = rawObjectId;
        }
    }
}
