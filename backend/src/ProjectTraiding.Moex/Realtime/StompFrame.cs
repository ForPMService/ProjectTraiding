using System.Text;

namespace ProjectTraiding.Moex.Realtime;

/// <summary>
/// Минимальный поднабор кадрирования STOMP, достаточный для потока Московской биржи.
/// Не универсальный клиент STOMP 1.2: транзакции, подтверждения приёма сообщений и
/// заголовок content-length не поддерживаются — бирже они не нужны.
///
/// Устройство кадра:
///   КОМАНДА              перевод строки
///   заголовок:значение   перевод строки   (ноль или более раз)
///                        перевод строки   (пустая строка — конец заголовков)
///   тело                 (может отсутствовать)
///   нулевой байт
///   переводы строк       (ноль или более — стандарт их допускает)
///
/// Тело бывает только у кадров SEND, MESSAGE и ERROR. У CONNECTED и RECEIPT тела нет —
/// всё существенное лежит у них в заголовках, и это не оплошность биржи, а требование
/// стандарта.
/// </summary>
public readonly record struct StompFrame(
    string Command,
    IReadOnlyDictionary<string, string> Headers,
    string Body)
{
    private const char NullTerminator = '\0';

    /// <summary>Кадр в байты: UTF-8 плюс завершающий нулевой байт.</summary>
    public static byte[] Serialize(StompFrame frame)
    {
        StringBuilder builder = new StringBuilder(256);

        builder.Append(frame.Command).Append('\n');

        foreach (KeyValuePair<string, string> header in frame.Headers)
        {
            builder.Append(header.Key).Append(':').Append(header.Value).Append('\n');
        }

        builder.Append('\n');
        builder.Append(frame.Body);
        builder.Append(NullTerminator);

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Сообщение является сердцебиением: пусто либо состоит только из корректных окончаний
    /// строк. Кадром не является и в счётчики не попадает. Проверять ДО вызова Parse.
    /// </summary>
    public static bool IsHeartbeat(string raw) => ContainsOnlyEndOfLines(raw.AsSpan());

    /// <summary>
    /// Только корректные окончания строк. По стандарту окончание строки — это перевод
    /// строки, которому МОЖЕТ предшествовать возврат каретки. Одиночный возврат каретки
    /// окончанием строки не является, и принимать его за сердцебиение — ошибка.
    /// </summary>
    private static bool ContainsOnlyEndOfLines(ReadOnlySpan<char> value)
    {
        int index = 0;

        while (index < value.Length)
        {
            if (value[index] == '\n')
            {
                index++;
                continue;
            }

            if (value[index] == '\r'
                && index + 1 < value.Length
                && value[index + 1] == '\n')
            {
                index += 2;
                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Разбор полного текста сообщения в кадр. Испорченное сообщение — FormatException
    /// с внятным указанием, что именно не сошлось: пробник существует ради разведки,
    /// и молчаливое проглатывание мусора лишает его смысла.
    /// </summary>
    public static StompFrame Parse(string raw)
    {
        if (IsHeartbeat(raw))
        {
            throw new FormatException(
                "STOMP: сообщение является сердцебиением и кадром не является. " +
                "Вызывайте IsHeartbeat до Parse.");
        }

        int terminator = raw.IndexOf(NullTerminator);
        if (terminator < 0)
        {
            throw new FormatException("STOMP: кадр не завершён нулевым байтом.");
        }

        if (!ContainsOnlyEndOfLines(raw.AsSpan(terminator + 1)))
        {
            throw new FormatException(
                "STOMP: после завершителя кадра присутствуют посторонние данные.");
        }

        string frameText = raw[..terminator];

        int commandEnd = frameText.IndexOf('\n');
        if (commandEnd < 0)
        {
            throw new FormatException("STOMP: в кадре отсутствует строка команды.");
        }

        string command = TrimCarriageReturn(frameText[..commandEnd]);
        if (command.Length == 0)
        {
            throw new FormatException("STOMP: команда кадра пуста.");
        }

        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal);
        int position = commandEnd + 1;
        bool headersClosed = false;

        while (position < frameText.Length)
        {
            int lineEnd = frameText.IndexOf('\n', position);
            if (lineEnd < 0)
            {
                throw new FormatException(
                    "STOMP: блок заголовков не завершён пустой строкой.");
            }

            string line = TrimCarriageReturn(frameText[position..lineEnd]);
            position = lineEnd + 1;

            if (line.Length == 0)
            {
                headersClosed = true;
                break;
            }

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new FormatException(
                    $"STOMP: строка заголовка без двоеточия или с пустым именем: {line}");
            }

            string name = line[..colon];
            string value = line[(colon + 1)..];
            headers.TryAdd(name, value);
        }

        if (!headersClosed)
        {
            throw new FormatException(
                "STOMP: блок заголовков не завершён пустой строкой.");
        }

        string body = position < frameText.Length
            ? frameText[position..]
            : string.Empty;

        return new StompFrame(command, headers, body);
    }

    private static string TrimCarriageReturn(string line) =>
        line.EndsWith('\r') ? line[..^1] : line;
}
