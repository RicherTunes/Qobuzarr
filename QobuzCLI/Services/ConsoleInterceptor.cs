using System;
using System.IO;
using System.Text;

namespace QobuzCLI.Services;

/// <summary>
/// A TextWriter that can selectively suppress or allow console output
/// </summary>
public class ConsoleInterceptor : TextWriter
{
    private readonly TextWriter _originalWriter;
    private bool _suppressOutput = false;

    public ConsoleInterceptor(TextWriter originalWriter)
    {
        _originalWriter = originalWriter;
    }

    public bool SuppressOutput
    {
        get => _suppressOutput;
        set => _suppressOutput = value;
    }

    public override Encoding Encoding => _originalWriter.Encoding;

    public override void Write(char value)
    {
        if (!_suppressOutput)
            _originalWriter.Write(value);
    }

    public override void Write(string? value)
    {
        // Allow AnsiConsole escape sequences through even when suppressing
        if (!_suppressOutput || (value != null && IsAnsiEscapeSequence(value)))
            _originalWriter.Write(value);
    }

    public override void WriteLine(string? value)
    {
        // Allow AnsiConsole escape sequences through even when suppressing
        if (!_suppressOutput || (value != null && IsAnsiEscapeSequence(value)))
            _originalWriter.WriteLine(value);
    }

    public override void WriteLine()
    {
        if (!_suppressOutput)
            _originalWriter.WriteLine();
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (!_suppressOutput)
            _originalWriter.Write(buffer, index, count);
    }

    public override void Flush()
    {
        if (!_suppressOutput)
            _originalWriter.Flush();
    }

    /// <summary>
    /// Checks if a string contains ANSI escape sequences (used by Spectre.Console)
    /// </summary>
    private static bool IsAnsiEscapeSequence(string value)
    {
        // Allow ANSI escape sequences to pass through for dashboard display
        return value.Contains('\x1b') || value.Contains("\u001b") ||
               value.StartsWith("\x1b[") || value.StartsWith("\u001b[");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _suppressOutput = false;
            _originalWriter.Flush();
        }
        base.Dispose(disposing);
    }
}
