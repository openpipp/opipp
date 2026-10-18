using System.Globalization;
using System.Text;

namespace Pipp.Core.Formats;

/// <summary>Raised when a stream does not contain a structurally valid FITS header or image.</summary>
public sealed class FitsFormatException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public FitsFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a descriptive message and inner exception.</summary>
    public FitsFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Raised when an image HDU declares more pixel data than the file actually contains.</summary>
public sealed class FitsTruncationException : Exception
{
    /// <summary>Creates the exception describing the short image payload.</summary>
    public FitsTruncationException(string message, long expectedBytes, long availableBytes) : base(message)
    {
        ExpectedBytes = expectedBytes;
        AvailableBytes = availableBytes;
    }

    /// <summary>Number of data bytes the header says should exist.</summary>
    public long ExpectedBytes { get; }

    /// <summary>Number of data bytes actually available at the image data offset.</summary>
    public long AvailableBytes { get; }
}

/// <summary>The value type stored in a FITS header card.</summary>
public enum FitsValueKind
{
    /// <summary>No value (for example the END card or a value-less keyword).</summary>
    None,

    /// <summary>A quoted character string.</summary>
    String,

    /// <summary>A FITS logical value (T or F).</summary>
    Logical,

    /// <summary>An integral value.</summary>
    Integer,

    /// <summary>A floating point value.</summary>
    Real,

    /// <summary>A commentary card (COMMENT, HISTORY or a blank keyword).</summary>
    Commentary,
}

/// <summary>
/// One 80-character FITS header card: an 8-character keyword, an optional "= " value indicator,
/// a value field and an optional slash-prefixed comment. Formatting and parsing follow the FITS 4.0
/// fixed-column rules.
/// </summary>
public sealed class FitsCard
{
    /// <summary>Width of a FITS header card in bytes.</summary>
    public const int CardSize = 80;

    /// <summary>FITS header block size; headers are always a multiple of this many bytes.</summary>
    public const int BlockSize = 2880;

    private readonly bool _hasEquals;
    private readonly string? _text;

    /// <summary>
    /// Creates a value card. Supported value types are <see cref="string"/>, <see cref="bool"/>,
    /// integral types and floating point types. Integer values are normalized to
    /// <see cref="long"/> and floating point values to <see cref="double"/>.
    /// </summary>
    /// <param name="keyword">FITS keyword, 1 to 8 characters, letters/digits/underscore/hyphen.</param>
    /// <param name="value">Value to store; may be null for a value-less card.</param>
    /// <param name="comment">Optional comment text written after " / ".</param>
    public FitsCard(string keyword, object? value, string? comment = null)
    {
        Keyword = ValidateKeyword(keyword);
        (Kind, Value) = Classify(value);
        Comment = ValidateText(comment, nameof(comment));
        _hasEquals = true;
    }

    private FitsCard(string keyword, FitsValueKind kind, object? value, string? comment, string? text, bool hasEquals)
    {
        Keyword = keyword;
        Kind = kind;
        Value = value;
        Comment = comment;
        _text = text;
        _hasEquals = hasEquals;
    }

    /// <summary>The normalized (upper case, space-trimmed) keyword occupying columns 1-8.</summary>
    public string Keyword { get; }

    /// <summary>The type of <see cref="Value"/>.</summary>
    public FitsValueKind Kind { get; }

    /// <summary>The parsed value, or null for commentary and value-less cards.</summary>
    public object? Value { get; }

    /// <summary>The comment text without the leading slash, or null.</summary>
    public string? Comment { get; }

    /// <summary>Commentary text for COMMENT/HISTORY/blank cards; otherwise null.</summary>
    public string? Text => _text;

    /// <summary>True when the card carries a typed value.</summary>
    public bool HasValue => Kind is FitsValueKind.String or FitsValueKind.Logical
        or FitsValueKind.Integer or FitsValueKind.Real;

    /// <summary>True for COMMENT, HISTORY and blank-keyword cards.</summary>
    public bool IsCommentary => Kind == FitsValueKind.Commentary;

    /// <summary>True for the END card that terminates a header.</summary>
    public bool IsEnd => !_hasEquals && Keyword == "END";

    /// <summary>Creates a COMMENT/HISTORY/blank commentary card.</summary>
    /// <param name="keyword">COMMENT, HISTORY or an empty string.</param>
    /// <param name="text">Commentary text, written starting at column 9.</param>
    /// <returns>A commentary card.</returns>
    public static FitsCard Commentary(string keyword, string text)
    {
        string kw = keyword.Length == 0 ? string.Empty : ValidateKeyword(keyword);
        return new FitsCard(kw, FitsValueKind.Commentary, null, null,
            ValidateText(text, nameof(text)) ?? string.Empty, hasEquals: false);
    }

    /// <summary>Creates the END card.</summary>
    /// <returns>The END card.</returns>
    public static FitsCard End() => new("END", FitsValueKind.None, null, null, null, hasEquals: false);

    /// <summary>Parses exactly one 80-byte header card.</summary>
    /// <param name="card">The card text; shorter strings are treated as space padded.</param>
    /// <returns>The parsed card.</returns>
    /// <exception cref="FitsFormatException">The card is not valid FITS syntax.</exception>
    public static FitsCard Parse(string card)
    {
        ArgumentNullException.ThrowIfNull(card);
        string text = card.Length >= CardSize ? card[..CardSize] : card.PadRight(CardSize);

        string keyword = text[..8].Trim();
        if (keyword.Length == 0)
        {
            return new FitsCard(string.Empty, FitsValueKind.Commentary, null, null,
                text[8..].TrimEnd(), hasEquals: false);
        }

        keyword = keyword.ToUpperInvariant();
        if (keyword == "END" && text[8..].Trim().Length == 0)
        {
            return End();
        }

        if (text[8] != '=')
        {
            // COMMENT/HISTORY and other non-value cards: text starts at column 9.
            return new FitsCard(keyword, FitsValueKind.Commentary, null, null,
                text[8..].TrimEnd(), hasEquals: false);
        }

        string field = text[10..];
        string work = field.TrimStart();
        if (work.StartsWith('\''))
        {
            var value = new StringBuilder();
            int i = 1;
            bool closed = false;
            while (i < work.Length)
            {
                char c = work[i];
                if (c == '\'')
                {
                    if (i + 1 < work.Length && work[i + 1] == '\'')
                    {
                        value.Append('\'');
                        i += 2;
                        continue;
                    }

                    i++;
                    closed = true;
                    break;
                }

                value.Append(c);
                i++;
            }

            if (!closed)
            {
                // Be tolerant of a missing closing quote: the remainder is the value.
            }

            string rest = i < work.Length ? work[i..] : string.Empty;
            return new FitsCard(keyword, FitsValueKind.String, value.ToString(), ExtractComment(rest), null, true);
        }

        int end = 0;
        while (end < work.Length && !char.IsWhiteSpace(work[end]) && work[end] != '/')
        {
            end++;
        }

        string token = work[..end];
        string remainder = work[end..];
        string? comment = ExtractComment(remainder);

        if (token.Length == 0)
        {
            return new FitsCard(keyword, FitsValueKind.None, null, comment, null, true);
        }

        if (token is "T" or "F")
        {
            return new FitsCard(keyword, FitsValueKind.Logical, token == "T", comment, null, true);
        }

        if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
        {
            return new FitsCard(keyword, FitsValueKind.Integer, integer, comment, null, true);
        }

        string normalized = token.Replace('D', 'E').Replace('d', 'e');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
        {
            return new FitsCard(keyword, FitsValueKind.Real, real, comment, null, true);
        }

        throw new FitsFormatException($"Unrecognised value '{token}' for FITS keyword {keyword}.");
    }

    /// <summary>Formats the card as exactly 80 characters with fixed FITS columns.</summary>
    /// <returns>An 80-character card string.</returns>
    /// <exception cref="FitsFormatException">The value cannot be represented in one card.</exception>
    public string Format()
    {
        string keyword = Keyword.PadRight(8);

        if (Kind == FitsValueKind.Commentary)
        {
            return PadOrTrim(keyword + (_text ?? string.Empty));
        }

        if (!_hasEquals)
        {
            return keyword.PadRight(CardSize);
        }

        var sb = new StringBuilder(CardSize);
        sb.Append(keyword).Append("= ");

        switch (Kind)
        {
            case FitsValueKind.String:
            {
                string token = "'" + ((string)Value!).Replace("'", "''") + "'";
                sb.Append(token);
                if (sb.Length > CardSize)
                {
                    throw new FitsFormatException($"String value for FITS keyword {Keyword} does not fit in one card.");
                }

                if (sb.Length < 30)
                {
                    sb.Append(' ', 30 - sb.Length);
                }

                break;
            }

            case FitsValueKind.Logical:
            {
                // FITS logicals are right justified: T/F sits in column 30.
                sb.Append(((bool)Value! ? "T" : "F").PadLeft(20));
                break;
            }

            case FitsValueKind.Integer:
            {
                string token = ((long)Value!).ToString(CultureInfo.InvariantCulture);
                if (token.Length > 20)
                {
                    throw new FitsFormatException($"Integer value for FITS keyword {Keyword} does not fit the value field.");
                }

                sb.Append(token.PadLeft(20));
                break;
            }

            case FitsValueKind.Real:
            {
                string token = FormatReal((double)Value!);
                if (token.Length > 20)
                {
                    throw new FitsFormatException($"Real value for FITS keyword {Keyword} does not fit the value field.");
                }

                sb.Append(token.PadLeft(20));
                break;
            }

            default:
                // Value-less card with an equals sign: leave the 20-character value field blank.
                sb.Append(' ', 20);
                break;
        }

        if (!string.IsNullOrWhiteSpace(Comment))
        {
            sb.Append(" / ").Append(Comment);
        }

        return PadOrTrim(sb.ToString());
    }

    /// <inheritdoc />
    public override string ToString() => Format();

    private static string FormatReal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new FitsFormatException("FITS real values must be finite.");
        }

        string text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.IndexOfAny(['.', 'E', 'e']) < 0 ? text + ".0" : text;
    }

    private static string PadOrTrim(string text)
    {
        if (text.Length == CardSize)
        {
            return text;
        }

        if (text.Length < CardSize)
        {
            return text.PadRight(CardSize);
        }

        // Overflow can only come from a comment (values are checked above); truncate it.
        return text[..CardSize];
    }

    private static string? ExtractComment(string remainder)
    {
        int slash = remainder.IndexOf('/');
        if (slash < 0)
        {
            return null;
        }

        string comment = remainder[(slash + 1)..].Trim();
        return comment.Length == 0 ? null : comment;
    }

    private static (FitsValueKind Kind, object? Value) Classify(object? value)
    {
        switch (value)
        {
            case null:
                return (FitsValueKind.None, null);
            case string s:
                EnsureSingleLine(s, "value");
                return (FitsValueKind.String, s);
            case bool b:
                return (FitsValueKind.Logical, b);
            case sbyte or byte or short or ushort or int or uint or long:
                return (FitsValueKind.Integer, Convert.ToInt64(value, CultureInfo.InvariantCulture));
            case ulong ul when ul <= long.MaxValue:
                return (FitsValueKind.Integer, (long)ul);
            case ulong ul:
                return (FitsValueKind.Real, (double)ul);
            case float f:
                return (FitsValueKind.Real, (double)f);
            case double d:
                return (FitsValueKind.Real, d);
            case decimal m:
                return (FitsValueKind.Real, (double)m);
            default:
                throw new ArgumentException(
                    $"unsupported FITS value type {value.GetType().Name}", nameof(value));
        }
    }

    private static string ValidateKeyword(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        keyword = keyword.Trim().ToUpperInvariant();
        if (keyword.Length is 0 or > 8)
        {
            throw new ArgumentException("FITS keywords must be 1 to 8 characters long.", nameof(keyword));
        }

        foreach (char c in keyword)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '_' && c != '-')
            {
                throw new ArgumentException($"invalid character '{c}' in FITS keyword.", nameof(keyword));
            }
        }

        return keyword;
    }

    private static string? ValidateText(string? text, string parameterName)
    {
        if (text is null)
        {
            return null;
        }

        EnsureSingleLine(text, parameterName);
        return text;
    }

    private static void EnsureSingleLine(string text, string parameterName)
    {
        foreach (char c in text)
        {
            if (c is '\r' or '\n' or '\0')
            {
                throw new ArgumentException("FITS card text cannot contain line breaks or NUL characters.", parameterName);
            }
        }
    }
}

/// <summary>
/// An ordered collection of <see cref="FitsCard"/> values with typed accessors and
/// 2880-byte block serialization.
/// </summary>
public sealed class FitsHeader
{
    private readonly List<FitsCard> _cards = new();

    /// <summary>The cards in header order.</summary>
    public IReadOnlyList<FitsCard> Cards => _cards;

    /// <summary>Number of cards (excluding the implicit END card).</summary>
    public int Count => _cards.Count;

    /// <summary>Appends a card.</summary>
    public void Add(FitsCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        _cards.Add(card);
    }

    /// <summary>Appends a value card.</summary>
    /// <param name="keyword">FITS keyword.</param>
    /// <param name="value">Card value.</param>
    /// <param name="comment">Optional comment.</param>
    public void Add(string keyword, object? value, string? comment = null) => Add(new FitsCard(keyword, value, comment));

    /// <summary>Appends a COMMENT (or other commentary) card.</summary>
    /// <param name="text">Commentary text.</param>
    /// <param name="keyword">COMMENT by default; HISTORY is also common.</param>
    public void AddComment(string text, string keyword = "COMMENT") => Add(FitsCard.Commentary(keyword, text));

    /// <summary>Returns the first card with the given keyword, or null.</summary>
    public FitsCard? Find(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        foreach (FitsCard card in _cards)
        {
            if (string.Equals(card.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>True when a card with the keyword exists.</summary>
    public bool Contains(string keyword) => Find(keyword) is not null;

    /// <summary>Gets a string value, or null.</summary>
    public string? GetString(string keyword) => Find(keyword)?.Value as string;

    /// <summary>Gets an integral value, or null when absent or not integral.</summary>
    public long? GetInteger(string keyword)
    {
        object? value = Find(keyword)?.Value;
        return value switch
        {
            long l => l,
            int i => i,
            short s => s,
            byte b => b,
            double d when d == Math.Floor(d) && d is >= long.MinValue and <= long.MaxValue => (long)d,
            _ => null,
        };
    }

    /// <summary>Gets a real value, or null when absent or not numeric.</summary>
    public double? GetReal(string keyword)
    {
        object? value = Find(keyword)?.Value;
        return value switch
        {
            long l => l,
            double d => d,
            _ => null,
        };
    }

    /// <summary>Gets a logical value, or null when absent or not logical.</summary>
    public bool? GetLogical(string keyword) => Find(keyword)?.Value as bool?;

    /// <summary>Gets an integral value with a fallback.</summary>
    public long GetLong(string keyword, long fallback) => GetInteger(keyword) ?? fallback;

    /// <summary>Gets a 32-bit integral value with a fallback.</summary>
    /// <exception cref="FitsFormatException">The value does not fit in a 32-bit integer.</exception>
    public int GetInt32(string keyword, int fallback)
    {
        long? value = GetInteger(keyword);
        if (value is null)
        {
            return fallback;
        }

        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new FitsFormatException($"FITS keyword {keyword} value {value} does not fit in a 32-bit integer.");
        }

        return (int)value.Value;
    }

    /// <summary>Replaces the first card with the keyword, or appends a new card.</summary>
    public void Set(string keyword, object? value, string? comment = null)
    {
        var replacement = new FitsCard(keyword, value, comment);
        for (int i = 0; i < _cards.Count; i++)
        {
            if (string.Equals(_cards[i].Keyword, replacement.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                _cards[i] = replacement;
                return;
            }
        }

        _cards.Add(replacement);
    }

    /// <summary>Removes all cards with the keyword; returns the number removed.</summary>
    public int Remove(string keyword) =>
        _cards.RemoveAll(c => string.Equals(c.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>Formats all cards plus END, without block padding.</summary>
    public string ToText()
    {
        var sb = new StringBuilder(_cards.Count * FitsCard.CardSize + FitsCard.CardSize);
        bool hasEnd = false;
        foreach (FitsCard card in _cards)
        {
            hasEnd |= card.IsEnd;
            sb.Append(card.Format());
        }

        if (!hasEnd)
        {
            sb.Append(FitsCard.End().Format());
        }

        return sb.ToString();
    }

    /// <summary>Serializes the header and pads it with spaces to a 2880-byte boundary.</summary>
    public byte[] ToBytes()
    {
        string text = ToText();
        int remainder = text.Length % FitsCard.BlockSize;
        if (remainder != 0)
        {
            text = text.PadRight(text.Length + (FitsCard.BlockSize - remainder));
        }

        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>Parses a header from a byte span, stopping at the END card.</summary>
    /// <param name="bytes">Buffer containing at least one complete header.</param>
    /// <param name="bytesConsumed">Number of bytes read, including 2880-byte block padding.</param>
    public static FitsHeader Parse(byte[] bytes, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        return Read(stream, out bytesConsumed);
    }

    /// <summary>
    /// Reads one header from the current stream position, block by block, until the END card.
    /// The stream is left immediately after the header block(s).
    /// </summary>
    /// <param name="stream">Seekable or forward-only stream positioned at the header start.</param>
    /// <param name="bytesConsumed">Number of header bytes read (a multiple of 2880 for intact files).</param>
    /// <returns>The parsed header.</returns>
    /// <exception cref="FitsFormatException">The stream ends before an END card is found.</exception>
    public static FitsHeader Read(Stream stream, out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new FitsHeader();
        var block = new byte[FitsCard.BlockSize];
        bytesConsumed = 0;

        while (true)
        {
            int read = ReadBlock(stream, block);
            if (read == 0)
            {
                throw new FitsFormatException("Unexpected end of file while reading a FITS header.");
            }

            bytesConsumed += read;
            for (int offset = 0; offset + FitsCard.CardSize <= read; offset += FitsCard.CardSize)
            {
                string cardText = Encoding.Latin1.GetString(block, offset, FitsCard.CardSize);
                if (string.IsNullOrWhiteSpace(cardText))
                {
                    continue;
                }

                FitsCard card = FitsCard.Parse(cardText);
                if (card.IsEnd)
                {
                    return header;
                }

                header._cards.Add(card);
            }

            if (read < FitsCard.BlockSize)
            {
                throw new FitsFormatException("FITS header ended before the END card was found.");
            }
        }
    }

    private static int ReadBlock(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
