using System.Globalization;
using System.Text;
using EdiX12.Core;

namespace EdiX12.Cli;

/// <summary>
/// The human-readable renderings: aligned columns, colour where the terminal takes it, and
/// exactly the same text with the escapes removed where it does not.
/// </summary>
/// <remarks>
/// Column widths are measured from the content rather than hard-coded, because the widest
/// diagnostic code is a detail of the library that will change as codes are added, and a
/// table that goes crooked the first time it is extended is worse than no table.
/// </remarks>
internal static class TextOutput
{
    /// <summary>Left margin on every row, so the header stands clear of the table.</summary>
    private const string Margin = "  ";

    /// <summary>Gap between columns. One space does not read as a column boundary.</summary>
    private const string Gutter = "   ";

    /// <summary>
    /// Renders the four delimiters with the ISA offset each one was read from.
    /// </summary>
    /// <param name="label">What to call the input — a path, or <c>&lt;stdin&gt;</c>.</param>
    /// <param name="isaVersion">ISA12, which is what decides whether ISA11 is a delimiter.</param>
    /// <param name="delimiters">The delimiters as read.</param>
    /// <param name="ansi">The palette.</param>
    internal static string Delimiters(string label, string isaVersion, X12Delimiters delimiters, Ansi ansi)
    {
        var rows = new List<(string Name, string Value, string Note)>
        {
            ("element separator", Describe(delimiters.Element),
                "ISA offset 3, the one delimiter readable without the others"),
            ("component separator", Describe(delimiters.Component),
                "ISA16 - at offset 104, not offset 16"),
            ("segment terminator", Describe(delimiters.Segment),
                "offset 105, the character after ISA16; no element names it"),
            ("repetition separator", delimiters.Repetition.HasValue ? Describe(delimiters.Repetition.Value) : "-",
                RepetitionNote(delimiters.Repetition.HasValue, isaVersion)),
        };

        int nameWidth = rows.Max(r => r.Name.Length);
        int valueWidth = rows.Max(r => r.Value.Length);

        var text = new StringBuilder();
        text.Append(Header(label, ansi))
            .Append(Gutter)
            .Append(ansi.Paint(ansi.Dim, "ISA12 " + (isaVersion.Length == 0 ? "(absent)" : isaVersion)))
            .Append('\n')
            .Append('\n');

        foreach ((string name, string value, string note) in rows)
        {
            text.Append(Margin)
                .Append(name.PadRight(nameWidth))
                .Append(Gutter)
                .Append(ansi.Paint(ansi.Bold + ansi.Yellow, value))
                .Append(new string(' ', valueWidth - value.Length))
                .Append(Gutter)
                .Append(ansi.Paint(ansi.Dim, note))
                .Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Renders the diagnostics as a table, with long messages wrapped and hanging-indented
    /// to the message column so the codes stay scannable down the left edge.
    /// </summary>
    /// <param name="label">What to call the input.</param>
    /// <param name="diagnostics">What <see cref="Interchange.Validate"/> returned.</param>
    /// <param name="width">Total line width to wrap to.</param>
    /// <param name="ansi">The palette.</param>
    internal static string Diagnostics(
        string label,
        IReadOnlyList<X12Diagnostic> diagnostics,
        int width,
        Ansi ansi)
    {
        var text = new StringBuilder();
        text.Append(Header(label, ansi)).Append('\n').Append('\n');

        if (diagnostics.Count == 0)
        {
            text.Append(Margin)
                .Append(ansi.Paint(ansi.Green, "OK"))
                .Append("  no diagnostics - the envelope is structurally sound.\n")
                .Append('\n')
                .Append(ansi.Paint(ansi.Dim, "Envelope checks only. This says nothing about the business data.\n"));
            return text.ToString();
        }

        string[] positions = diagnostics
            .Select(d => "segment " + d.SegmentPosition.ToString(CultureInfo.InvariantCulture))
            .ToArray();

        int codeWidth = diagnostics.Max(d => d.Code.Length);
        int positionWidth = positions.Max(p => p.Length);

        int indent = Margin.Length + codeWidth + Gutter.Length + positionWidth + Gutter.Length;
        int messageWidth = Math.Max(24, width - indent);
        string hangingIndent = new string(' ', indent);

        for (int i = 0; i < diagnostics.Count; i++)
        {
            X12Diagnostic diagnostic = diagnostics[i];
            string[] lines = Wrap(diagnostic.Message, messageWidth);

            text.Append(Margin)
                .Append(ansi.Paint(ansi.Bold + ansi.Yellow, diagnostic.Code))
                .Append(new string(' ', codeWidth - diagnostic.Code.Length))
                .Append(Gutter)
                .Append(ansi.Paint(ansi.Dim, positions[i]))
                .Append(new string(' ', positionWidth - positions[i].Length))
                .Append(Gutter)
                .Append(lines[0])
                .Append('\n');

            for (int line = 1; line < lines.Length; line++)
            {
                text.Append(hangingIndent).Append(lines[line]).Append('\n');
            }
        }

        string summary = diagnostics.Count == 1 ? "1 diagnostic" : diagnostics.Count + " diagnostics";
        text.Append('\n').Append(ansi.Paint(ansi.Bold + ansi.Red, summary)).Append('\n');

        return text.ToString();
    }

    /// <summary>Renders a delimiter readably, since it is frequently a control character.</summary>
    internal static string Describe(char c) => c switch
    {
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        ' ' => "<space>",
        _ => c.ToString(),
    };

    /// <summary>Greedy word wrap. Words longer than the column are left to overhang.</summary>
    internal static string[] Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = new StringBuilder();

        foreach (string word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                lines.Add(line.ToString());
                line.Clear();
            }

            if (line.Length > 0)
            {
                line.Append(' ');
            }

            line.Append(word);
        }

        if (line.Length > 0 || lines.Count == 0)
        {
            lines.Add(line.ToString());
        }

        return lines.ToArray();
    }

    private static string Header(string label, Ansi ansi) => ansi.Paint(ansi.Bold + ansi.Cyan, label);

    /// <summary>
    /// Explains the repetition separator, including why there isn't one. The absence is the
    /// interesting case: before 00501 ISA11 is the Interchange Control Standards Identifier
    /// and reading it as a delimiter splits element values on the letter U.
    /// </summary>
    private static string RepetitionNote(bool present, string isaVersion)
    {
        if (present)
        {
            return "ISA11 at offset 82, a delimiter only from 00501 onward";
        }

        bool preRepetition = isaVersion.Length == 5 && string.CompareOrdinal(isaVersion, "00501") < 0;

        return preRepetition
            ? $"none - ISA12 is {isaVersion}, where ISA11 is the standards identifier"
            : "none - ISA11 declares no repetition separator";
    }
}
