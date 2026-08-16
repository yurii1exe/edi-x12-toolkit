namespace EdiX12.Core;

/// <summary>
/// The four delimiter characters that an X12 interchange declares about itself.
/// </summary>
/// <remarks>
/// <para>
/// These are <em>not</em> constants. <c>*</c> and <c>~</c> are common defaults, not rules.
/// Every interchange states its own delimiters in its ISA segment, and a parser is
/// required to read them from there. Hard-coding them is the single most common defect
/// in open-source X12 code, and it shows up the first time a partner sends a file
/// delimited with <c>|</c> or with a newline as the segment terminator.
/// </para>
/// <para>
/// A delimiter character may never appear inside element data. That is the whole
/// contract: the sender picks characters that its data does not contain.
/// </para>
/// </remarks>
public sealed class X12Delimiters
{
    /// <summary>Separates data elements within a segment. Declared at ISA offset 3.</summary>
    public char Element { get; }

    /// <summary>
    /// Separates component (sub-) elements within a composite element.
    /// Declared by ISA16, which is the last character of the fixed-width ISA segment.
    /// </summary>
    public char Component { get; }

    /// <summary>
    /// Terminates a segment. Declared implicitly: it is the character immediately
    /// following ISA16.
    /// </summary>
    public char Segment { get; }

    /// <summary>
    /// Separates repetitions of a repeating element. Declared by ISA11 in version
    /// 00501 (5010) and later; <see langword="null"/> for 4010 and earlier, where
    /// ISA11 carries the Interchange Control Standards Identifier instead, and also
    /// null when the sender sets ISA11 to <c>U</c> to mean "repetition not used".
    /// </summary>
    public char? Repetition { get; }

    /// <summary>Creates a delimiter set.</summary>
    /// <param name="element">Data element separator.</param>
    /// <param name="component">Component element separator.</param>
    /// <param name="segment">Segment terminator.</param>
    /// <param name="repetition">Repetition separator, or <see langword="null"/> if not in use.</param>
    public X12Delimiters(char element, char component, char segment, char? repetition = null)
    {
        Element = element;
        Component = component;
        Segment = segment;
        Repetition = repetition;
    }

    /// <summary>
    /// The conventional delimiters (<c>*</c>, <c>:</c>, <c>~</c>, <c>^</c>). Useful for
    /// <em>writing</em> a new interchange. Never assume them when reading one.
    /// </summary>
    public static X12Delimiters Default { get; } = new X12Delimiters('*', ':', '~', '^');

    /// <summary>Renders the delimiters for diagnostics, e.g. <c>element='*' component=':' segment='~'</c>.</summary>
    public override string ToString()
    {
        string rep = Repetition.HasValue ? $" repetition='{Describe(Repetition.Value)}'" : string.Empty;
        return $"element='{Describe(Element)}' component='{Describe(Component)}' " +
               $"segment='{Describe(Segment)}'{rep}";
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
}
