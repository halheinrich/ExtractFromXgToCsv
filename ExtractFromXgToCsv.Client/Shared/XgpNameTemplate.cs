using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ExtractFromXgToCsv.Client.Shared;

/// <summary>
/// A parsed .xgp name pattern: literal text interleaved with
/// <c>{token}</c> placeholders resolved against <see cref="XgpNameTokens"/>.
/// Parse once (<see cref="TryParse"/>), render many (<see cref="Render"/>).
/// All pattern-shaped failures are parse-time; <see cref="Render"/> never
/// fails — literals were validated at parse time and token output passes
/// through <see cref="Sanitize"/>.
///
/// <para>
/// Braces are placeholder delimiters only — there is no escape syntax for a
/// literal <c>'{'</c> or <c>'}'</c> in a pattern.
/// </para>
///
/// <para>
/// Renders the <b>base name</b> only: no <c>.xgp</c> extension and no
/// duplicate-name uniquifier — both are <see cref="XgpNameAllocator"/>'s job.
/// </para>
/// </summary>
public sealed class XgpNameTemplate
{
    /// <summary>
    /// Characters rejected in pattern literals and replaced with <c>'_'</c>
    /// in token output: the Windows-invalid filename set, checked explicitly
    /// so Web mode (browser-sandbox zip entries) and Local mode (server
    /// filesystem) enforce the same rule regardless of what OS the runtime
    /// reports. Control characters are treated the same way.
    /// </summary>
    public static IReadOnlyList<char> InvalidFileNameChars { get; } = [.. InvalidCharList];

    private const string InvalidCharList = "\"<>|:*?\\/";

    private static readonly SearchValues<char> _invalidChars =
        SearchValues.Create(InvalidCharList);

    private readonly IReadOnlyList<Segment> _segments;

    private XgpNameTemplate(IReadOnlyList<Segment> segments) => _segments = segments;

    /// <summary>
    /// Parses <paramref name="pattern"/> into a renderable template.
    /// Returns <see langword="false"/> with a user-displayable
    /// <paramref name="error"/> on the first violation found: an empty
    /// pattern, an unmatched or empty pair of braces, an unknown token name
    /// (the error lists the valid ones), or a literal containing a character
    /// not allowed in filenames.
    /// </summary>
    public static bool TryParse(
        string? pattern,
        [NotNullWhen(true)] out XgpNameTemplate? template,
        out string error)
    {
        template = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Name pattern must not be empty.";
            return false;
        }

        var segments = new List<Segment>();
        int pos = 0;
        while (pos < pattern.Length)
        {
            int open = pattern.IndexOf('{', pos);
            int close = pattern.IndexOf('}', pos);

            if (open < 0 && close < 0)
            {
                if (!TryAddLiteral(pattern[pos..], segments, out error))
                    return false;
                break;
            }
            if (close >= 0 && (open < 0 || close < open))
            {
                error = "Name pattern has a '}' with no matching '{'.";
                return false;
            }

            // A '{' at `open`, with any '}' known to come after it.
            if (!TryAddLiteral(pattern[pos..open], segments, out error))
                return false;
            if (close < 0)
            {
                error = "Name pattern has a '{' with no matching '}'.";
                return false;
            }

            var name = pattern[(open + 1)..close];
            if (name.Length == 0)
            {
                error = "Name pattern has an empty {} placeholder.";
                return false;
            }
            if (!XgpNameTokens.TryGet(name, out var token))
            {
                error = $"Unknown token '{{{name}}}' — valid tokens: "
                    + string.Join(", ", XgpNameTokens.All.Select(t => $"{{{t.Name}}}"))
                    + ".";
                return false;
            }

            segments.Add(new TokenSegment(token));
            pos = close + 1;
        }

        template = new XgpNameTemplate(segments);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Renders the base name (no extension, no uniquifier) for one decision.
    /// Never fails: token output is sanitized, and empty token values render
    /// as the empty string.
    /// </summary>
    public string Render(XgpNameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sb = new StringBuilder();
        foreach (var segment in _segments)
            sb.Append(segment.Render(context));
        return sb.ToString();
    }

    /// <summary>
    /// Replaces every filename-invalid or control character in a token's
    /// output with <c>'_'</c>. Literals never pass through here — they were
    /// rejected at parse time instead, so the user sees an error rather than
    /// a silently altered pattern.
    /// </summary>
    internal static string Sanitize(string value)
    {
        if (value.AsSpan().IndexOfAny(_invalidChars) < 0 && !value.Any(char.IsControl))
            return value;

        return string.Create(value.Length, value, static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                var c = source[i];
                chars[i] = InvalidCharList.Contains(c) || char.IsControl(c) ? '_' : c;
            }
        });
    }

    private static bool TryAddLiteral(string text, List<Segment> segments, out string error)
    {
        if (text.Length > 0)
        {
            if (text.AsSpan().IndexOfAny(_invalidChars) >= 0 || text.Any(char.IsControl))
            {
                error = "Name pattern contains characters not allowed in filenames.";
                return false;
            }
            segments.Add(new LiteralSegment(text));
        }
        error = string.Empty;
        return true;
    }

    private abstract record Segment
    {
        public abstract string Render(XgpNameContext context);
    }

    private sealed record LiteralSegment(string Text) : Segment
    {
        public override string Render(XgpNameContext context) => Text;
    }

    private sealed record TokenSegment(XgpNameToken Token) : Segment
    {
        public override string Render(XgpNameContext context) =>
            Sanitize(Token.Render(context));
    }
}
