using System.Globalization;
using System.Text;

namespace ProjectTraiding.Diagnostics.Protocol;

internal sealed class MoexStompFrameReader
{
    private const int MaximumFrameLength = 8 * 1024 * 1024;
    private byte[] _buffer = new byte[8192];
    private int _length;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (_length + bytes.Length > MaximumFrameLength)
        {
            throw new InvalidDataException($"STOMP-кадр превышает ограничение {MaximumFrameLength} байт.");
        }

        EnsureCapacity(_length + bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(_length));
        _length += bytes.Length;
    }

    public bool TryReadFrame(out MoexStompFrame frame)
    {
        SkipHeartbeatBytes();
        frame = null!;

        if (!TryFindHeaderEnd(out int headerEnd, out int bodyStart))
        {
            return false;
        }

        int commandEnd = FindByte((byte)'\n', 0, headerEnd);
        if (commandEnd < 0)
        {
            throw new InvalidDataException("В STOMP-кадре отсутствует перевод строки после команды.");
        }

        string command = DecodeLine(0, commandEnd);
        List<string> headers = ReadHeaders(commandEnd + 1, headerEnd);
        int contentLength = ReadContentLength(headers);
        int frameEnd;

        if (contentLength >= 0)
        {
            long terminatorIndex = (long)bodyStart + contentLength;
            if (terminatorIndex >= MaximumFrameLength)
            {
                throw new InvalidDataException($"STOMP-кадр превышает ограничение {MaximumFrameLength} байт.");
            }

            if (_length <= terminatorIndex)
            {
                return false;
            }

            if (_buffer[(int)terminatorIndex] != 0)
            {
                throw new InvalidDataException("STOMP-кадр с content-length не завершён нулевым байтом.");
            }

            frameEnd = (int)terminatorIndex;
        }
        else
        {
            frameEnd = FindByte(0, bodyStart, _length);
            if (frameEnd < 0)
            {
                return false;
            }
        }

        string body = Encoding.UTF8.GetString(_buffer, bodyStart, frameEnd - bodyStart);
        frame = new MoexStompFrame(command, headers, body);
        RemovePrefix(frameEnd + 1);
        return true;
    }

    private void EnsureCapacity(int requiredLength)
    {
        if (requiredLength <= _buffer.Length)
        {
            return;
        }

        int capacity = _buffer.Length;
        while (capacity < requiredLength)
        {
            capacity = Math.Min(capacity * 2, MaximumFrameLength);
        }

        byte[] expanded = new byte[capacity];
        _buffer.AsSpan(0, _length).CopyTo(expanded);
        _buffer = expanded;
    }

    private void SkipHeartbeatBytes()
    {
        int skipped = 0;
        while (skipped < _length && (_buffer[skipped] == (byte)'\n' || _buffer[skipped] == (byte)'\r'))
        {
            skipped++;
        }

        if (skipped > 0)
        {
            RemovePrefix(skipped);
        }
    }

    private bool TryFindHeaderEnd(out int headerEnd, out int bodyStart)
    {
        for (int index = 0; index < _length; index++)
        {
            if (_buffer[index] != (byte)'\n')
            {
                continue;
            }

            if (index + 1 < _length && _buffer[index + 1] == (byte)'\n')
            {
                headerEnd = index;
                bodyStart = index + 2;
                return true;
            }

            if (index + 2 < _length && _buffer[index + 1] == (byte)'\r' && _buffer[index + 2] == (byte)'\n')
            {
                headerEnd = index;
                bodyStart = index + 3;
                return true;
            }
        }

        headerEnd = 0;
        bodyStart = 0;
        return false;
    }

    private List<string> ReadHeaders(int start, int end)
    {
        List<string> headers = new List<string>();
        int lineStart = start;
        while (lineStart < end)
        {
            int lineEnd = FindByte((byte)'\n', lineStart, end);
            if (lineEnd < 0)
            {
                lineEnd = end;
            }

            string line = DecodeLine(lineStart, lineEnd);
            if (line.Length > 0)
            {
                headers.Add(line);
            }

            lineStart = lineEnd + 1;
        }

        return headers;
    }

    private int ReadContentLength(List<string> headers)
    {
        for (int index = 0; index < headers.Count; index++)
        {
            int colonIndex = headers[index].IndexOf(':');
            if (colonIndex < 0 || !headers[index].AsSpan(0, colonIndex).SequenceEqual("content-length"))
            {
                continue;
            }

            ReadOnlySpan<char> value = headers[index].AsSpan(colonIndex + 1);
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int contentLength) || contentLength < 0)
            {
                throw new InvalidDataException("STOMP-заголовок content-length содержит неверную длину.");
            }

            return contentLength;
        }

        return -1;
    }

    private string DecodeLine(int start, int end)
    {
        if (end > start && _buffer[end - 1] == (byte)'\r')
        {
            end--;
        }

        return Encoding.UTF8.GetString(_buffer, start, end - start);
    }

    private int FindByte(byte value, int start, int end)
    {
        for (int index = start; index < end; index++)
        {
            if (_buffer[index] == value)
            {
                return index;
            }
        }

        return -1;
    }

    private void RemovePrefix(int count)
    {
        int remaining = _length - count;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_buffer, count, _buffer, 0, remaining);
        }

        _length = remaining;
    }
}

internal sealed record MoexStompFrame(string Command, List<string> Headers, string Body)
{
    public string? GetHeader(string name)
    {
        for (int index = 0; index < Headers.Count; index++)
        {
            int colonIndex = Headers[index].IndexOf(':');
            if (colonIndex >= 0 && Headers[index].AsSpan(0, colonIndex).SequenceEqual(name))
            {
                return Headers[index][(colonIndex + 1)..];
            }
        }

        return null;
    }
}
