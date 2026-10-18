using System.Globalization;

namespace Opipp.Core.Diagnostics;

/// <summary>
/// The machine-readable tokens the original engine wrote to stdout for the PIPP GUI.
/// Values are reproduced verbatim (including the zero-padded formats) so the existing
/// WinForms front-end can drive a managed engine process without changes.
/// </summary>
public enum EngineToken
{
    Progress,
    File,
    FileLabel,
    FrameLabel,
    FrameNumber,
    FrameCount,
    FileNameLabel,
    XPosition,
    YPosition,
    QualitySort,
    QualitySortFrames,
    Animation,
    DetectThreshold,
    MaxPixel,
    MaxRedPixel,
    MaxGreenPixel,
    MaxBluePixel,
    Error,
}

/// <summary>Writes the legacy <c>@TOKEN=value</c> protocol lines to a text writer.</summary>
public sealed class EngineProtocolWriter
{
    private readonly TextWriter _out;
    private readonly object _gate = new();

    public EngineProtocolWriter(TextWriter output) => _out = output ?? throw new ArgumentNullException(nameof(output));

    private static string Name(EngineToken token) => token switch
    {
        EngineToken.Progress => "PROGRESS",
        EngineToken.File => "PFILE",
        EngineToken.FileLabel => "PFILELAB",
        EngineToken.FrameLabel => "PFRAMELAB",
        EngineToken.FrameNumber => "FNUM",
        EngineToken.FrameCount => "FCNT",
        EngineToken.FileNameLabel => "FNAMELAB",
        EngineToken.XPosition => "XPOS",
        EngineToken.YPosition => "YPOS",
        EngineToken.QualitySort => "QSORT",
        EngineToken.QualitySortFrames => "QSFRAMESLAB",
        EngineToken.Animation => "ANIM",
        EngineToken.DetectThreshold => "DETECT_THRESHOLD",
        EngineToken.MaxPixel => "MAX_PIXEL",
        EngineToken.MaxRedPixel => "MAX_R_PIXEL",
        EngineToken.MaxGreenPixel => "MAX_G_PIXEL",
        EngineToken.MaxBluePixel => "MAX_B_PIXEL",
        EngineToken.Error => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(token)),
    };

    /// <summary>Emits <c>@TOKEN=value</c> with invariant formatting, as a single line.</summary>
    public void Write(EngineToken token, string value)
    {
        lock (_gate)
        {
            _out.Write('@');
            _out.Write(Name(token));
            _out.Write('=');
            _out.WriteLine(value);
            _out.Flush();
        }
    }

    public void Write(EngineToken token, int value) =>
        Write(token, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Emits the "File x of y" label form used by the GUI for the current input file.</summary>
    public void WriteFileLabel(int index, int total) =>
        Write(EngineToken.FileLabel, string.Create(CultureInfo.InvariantCulture, $"File {index} of {total}"));

    /// <summary>Emits a free-form human progress line (no token prefix).</summary>
    public void WriteLine(string text)
    {
        lock (_gate)
        {
            _out.WriteLine(text);
            _out.Flush();
        }
    }
}

/// <summary>Parses the legacy stdout protocol lines.</summary>
public static class EngineProtocolParser
{
    /// <summary>
    /// Parses one line. Returns true for a recognised token (with its name and value);
    /// returns false for ordinary console text.
    /// </summary>
    public static bool TryParse(string line, out string token, out string value)
    {
        token = string.Empty;
        value = string.Empty;
        if (string.IsNullOrEmpty(line) || line[0] != '@')
        {
            return false;
        }

        int eq = line.IndexOf('=', StringComparison.Ordinal);
        if (eq < 0)
        {
            token = line[1..];
            return true;
        }

        token = line[1..eq];
        value = line[(eq + 1)..];
        return true;
    }
}
