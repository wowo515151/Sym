// Copyright Warren Harding 2026
using System.Text;

namespace ConsoleHelmsman;

public sealed class ConsoleLog
{
    private static string CleanForDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Best-effort removal of ANSI escape sequences.
        // Most common form: ESC [ ... command
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\u001b' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == '[')
                {
                    i += 2;
                    while (i < text.Length)
                    {
                        var ch = text[i];
                        if ((ch >= '@' && ch <= '~'))
                        {
                            break;
                        }
                        i++;
                    }
                    continue;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private readonly object _gate = new();
    private readonly char[] _buffer;
    private int _start;
    private int _length;

    public ConsoleLog(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new char[capacity];
    }

    public event EventHandler? Updated;

    public void AppendStdOut(string text)
    {
        AppendInternal(CleanForDisplay(text));
    }

    public void AppendStdErr(string text)
    {
        AppendInternal(CleanForDisplay(text));
    }

    public void AppendInput(string text)
    {
        AppendInternal(text);
    }

    public void AppendSystemMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var text = message.EndsWith(Environment.NewLine, StringComparison.Ordinal)
            ? message
            : message + Environment.NewLine;

        AppendInternal(text);
    }

    public string GetTail(int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        lock (_gate)
        {
            if (_length == 0)
            {
                return string.Empty;
            }

            var count = Math.Min(_length, maxChars);
            var result = new char[count];
            var startIndex = (_start + _length - count) % _buffer.Length;

            if (startIndex + count <= _buffer.Length)
            {
                Array.Copy(_buffer, startIndex, result, 0, count);
            }
            else
            {
                var firstPart = _buffer.Length - startIndex;
                Array.Copy(_buffer, startIndex, result, 0, firstPart);
                Array.Copy(_buffer, 0, result, firstPart, count - firstPart);
            }

            return new string(result);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _start = 0;
            _length = 0;
            // Optionally clear buffer contents for security/consistency.
            Array.Clear(_buffer, 0, _buffer.Length);
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void AppendInternal(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_gate)
        {
            foreach (var ch in text)
            {
                if (_length < _buffer.Length)
                {
                    var index = (_start + _length) % _buffer.Length;
                    _buffer[index] = ch;
                    _length++;
                }
                else
                {
                    _buffer[_start] = ch;
                    _start = (_start + 1) % _buffer.Length;
                }
            }
        }

        Updated?.Invoke(this, EventArgs.Empty);
    }
}
