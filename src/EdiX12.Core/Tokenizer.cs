namespace EdiX12.Core;

/// <summary>
/// Turns raw X12 text into a flat sequence of <see cref="Segment"/> values, using the
/// delimiters the interchange declares about itself.
/// </summary>
/// <remarks>
/// <para><b>Why this class exists.</b> Most X12 parsers begin with something like
/// <c>text.Split('~')</c> and then <c>segment.Split('*')</c>. That works until a partner
/// sends a file that uses different delimiters, which is entirely legal and happens
/// constantly. X12 has no fixed delimiters; each interchange declares its own.</para>
///
/// <para><b>How they are declared.</b> ISA is the only fixed-width segment in X12 —
/// exactly 106 characters including its terminator — and it is fixed-width precisely so a
/// reader can locate the delimiters by offset before it knows how to split anything.
/// Reading left to right from the start of the segment:</para>
///
/// <code>
/// offset 0..2    "ISA"
/// offset 3       the data element separator          &lt;-- read this first
/// offset 4..5    ISA01 Authorization Information Qualifier   (2)
/// offset 7..16   ISA02 Authorization Information             (10)
/// offset 18..19  ISA03 Security Information Qualifier        (2)
/// offset 21..30  ISA04 Security Information                  (10)
/// offset 32..33  ISA05 Interchange ID Qualifier              (2)
/// offset 35..49  ISA06 Interchange Sender ID                 (15)
/// offset 51..52  ISA07 Interchange ID Qualifier              (2)
/// offset 54..68  ISA08 Interchange Receiver ID               (15)
/// offset 70..75  ISA09 Interchange Date            YYMMDD    (6)
/// offset 77..80  ISA10 Interchange Time            HHMM      (4)
/// offset 82      ISA11 Repetition Separator (5010) /
///                      Interchange Control Standards Id (4010)  (1)
/// offset 84..88  ISA12 Interchange Control Version Number    (5)
/// offset 90..98  ISA13 Interchange Control Number            (9)
/// offset 100     ISA14 Acknowledgment Requested              (1)
/// offset 102     ISA15 Usage Indicator (P/T)                 (1)
/// offset 104     ISA16 Component Element Separator    &lt;-- and this
/// offset 105     the segment terminator               &lt;-- and this
/// </code>
///
/// <para>Note the trap in the wording of the specification: the component separator is
/// <em>element ISA16</em>, which lives at character offset 104 — not at offset 16, which
/// is the tail of ISA02. Both numbers are "16" and only one of them is right.</para>
///
/// <para>The segment terminator is never named by an element at all. It is simply whatever
/// character follows ISA16, which is why it has to be picked up here rather than assumed.
/// It is frequently <c>~</c>, but a newline and <c>|</c> are both in live use.</para>
/// </remarks>
public static class X12Tokenizer
{
    /// <summary>Length of a specification-compliant ISA segment, including its terminator.</summary>
    public const int IsaSegmentLength = 106;

    /// <summary>Offset of the data element separator within the ISA segment.</summary>
    private const int ElementSeparatorOffset = 3;

    /// <summary>Offset of ISA16, the component element separator, within the ISA segment.</summary>
    private const int ComponentSeparatorOffset = 104;

    /// <summary>Offset of the segment terminator within the ISA segment.</summary>
    private const int SegmentTerminatorOffset = 105;

    /// <summary>Number of data elements in an ISA segment, hence the number of separators in it.</summary>
    private const int IsaElementCount = 16;

    /// <summary>
    /// The lowest Interchange Control Version Number (ISA12) at which ISA11 carries the
    /// repetition separator. Before 5010 it carried the Interchange Control Standards
    /// Identifier, conventionally <c>U</c>.
    /// </summary>
    private const string FirstVersionWithRepetitionSeparator = "00501";

    /// <summary>
    /// Reads the delimiters an interchange declares in its ISA segment.
    /// </summary>
    /// <param name="input">Raw interchange text. Leading whitespace and a UTF-8 BOM are tolerated.</param>
    /// <returns>The declared delimiters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="X12ParseException">The input does not start with a readable ISA segment.</exception>
    public static X12Delimiters ReadDelimiters(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        return ReadIsa(input).Delimiters;
    }

    /// <summary>
    /// Reads every segment of an interchange, in order.
    /// </summary>
    /// <param name="input">Raw interchange text.</param>
    /// <returns>
    /// The segments, ISA first, each carrying its 1-based position within the interchange.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="X12ParseException">The input does not start with a readable ISA segment.</exception>
    public static IReadOnlyList<Segment> Tokenize(string input) => Tokenize(input, out _);

    /// <summary>
    /// Reads every segment of an interchange, in order, and reports the delimiters that
    /// were used to read them.
    /// </summary>
    /// <param name="input">Raw interchange text.</param>
    /// <param name="delimiters">The delimiters declared by the ISA segment.</param>
    /// <returns>
    /// The segments, ISA first, each carrying its 1-based position within the interchange.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="X12ParseException">The input does not start with a readable ISA segment.</exception>
    public static IReadOnlyList<Segment> Tokenize(string input, out X12Delimiters delimiters)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        IsaScan isa = ReadIsa(input);
        delimiters = isa.Delimiters;

        var segments = new List<Segment>
        {
            // The ISA is emitted like any other segment. Splitting it on the element
            // separator is safe even though it is fixed-width, because a delimiter
            // character may never occur inside element data.
            BuildSegment(input.Substring(isa.Start, isa.TerminatorIndex - isa.Start), delimiters, position: 1),
        };

        int cursor = isa.TerminatorIndex + 1;
        int position = 2;

        while (cursor < input.Length)
        {
            int terminator = input.IndexOf(delimiters.Segment, cursor);

            // A missing final terminator is a common sender defect. Take the trailing
            // text as a segment anyway rather than discarding it silently; the envelope
            // validation will report the real problem.
            int end = terminator < 0 ? input.Length : terminator;

            string raw = input.Substring(cursor, end - cursor);
            cursor = end + 1;

            // Line endings between segments are optional and inconsistent in the wild:
            // some senders write one segment per line, some write the whole interchange
            // on a single line, some do both in the same file. Strip whatever separates
            // segments visually; it is never part of the data.
            raw = raw.Trim('\r', '\n', ' ', '\t');
            if (raw.Length == 0)
            {
                continue;
            }

            segments.Add(BuildSegment(raw, delimiters, position));
            position++;
        }

        return segments;
    }

    /// <summary>Splits one segment's text into an identifier and its elements.</summary>
    private static Segment BuildSegment(string raw, X12Delimiters delimiters, int position)
    {
        string[] parts = raw.Split(delimiters.Element);
        string id = parts[0];

        var elements = new string[parts.Length - 1];
        Array.Copy(parts, 1, elements, 0, elements.Length);

        return new Segment(id, elements, delimiters, position);
    }

    /// <summary>Where the ISA is and what it declares.</summary>
    private readonly struct IsaScan
    {
        internal IsaScan(int start, int terminatorIndex, X12Delimiters delimiters)
        {
            Start = start;
            TerminatorIndex = terminatorIndex;
            Delimiters = delimiters;
        }

        /// <summary>Index of the <c>I</c> of <c>ISA</c>.</summary>
        internal int Start { get; }

        /// <summary>Index of the segment terminator that closes the ISA.</summary>
        internal int TerminatorIndex { get; }

        internal X12Delimiters Delimiters { get; }
    }

    /// <summary>
    /// Locates the ISA segment and extracts the delimiters from it.
    /// </summary>
    private static IsaScan ReadIsa(string input)
    {
        int start = FindIsaStart(input);

        // The element separator is the only delimiter that can be read without knowing
        // any of the others, which is why the specification puts it at a fixed offset
        // immediately after the segment identifier.
        char element = input[start + ElementSeparatorOffset];

        if (char.IsLetterOrDigit(element))
        {
            throw new X12ParseException(
                $"Character at ISA offset {ElementSeparatorOffset} is '{X12Delimiters.Describe(element)}', " +
                "which cannot be a data element separator. The interchange does not begin with a valid ISA segment.")
            {
                SegmentPosition = 1,
            };
        }

        int componentIndex = LocateComponentSeparator(input, start, element);
        int terminatorIndex = componentIndex + 1;

        if (terminatorIndex >= input.Length)
        {
            throw new X12ParseException(
                "ISA segment is truncated: the segment terminator that should follow ISA16 " +
                "(the component element separator) is past the end of the input.")
            {
                SegmentPosition = 1,
            };
        }

        char component = input[componentIndex];
        char terminator = input[terminatorIndex];

        // ISA12 tells us how to interpret ISA11, so the ISA has to be split before the
        // delimiter set can be finished. Split with a provisional set that has everything
        // except the repetition separator, which is exactly what is being decided here.
        string isaText = input.Substring(start, terminatorIndex - start);
        string[] parts = isaText.Split(element);

        if (parts.Length != IsaElementCount + 1)
        {
            throw new X12ParseException(
                $"ISA segment declares {parts.Length - 1} elements, expected {IsaElementCount}. " +
                $"Read with {new X12Delimiters(element, component, terminator)}.")
            {
                SegmentPosition = 1,
            };
        }

        char? repetition = ReadRepetitionSeparator(isa11: parts[11], isa12: parts[12], element);

        return new IsaScan(start, terminatorIndex, new X12Delimiters(element, component, terminator, repetition));
    }

    /// <summary>
    /// Decides whether ISA11 is a repetition separator, which depends on the version in ISA12.
    /// </summary>
    /// <param name="isa11">ISA11 as sent.</param>
    /// <param name="isa12">ISA12, the Interchange Control Version Number, e.g. <c>00501</c>.</param>
    /// <param name="elementSeparator">The element separator, used only for a sanity check.</param>
    private static char? ReadRepetitionSeparator(string isa11, string isa12, char elementSeparator)
    {
        if (isa11.Length != 1)
        {
            return null;
        }

        string version = isa12.Trim();

        // Ordinal comparison is correct here: ISA12 is a fixed five-digit numeric string,
        // so "00401" < "00501" < "00602" lexicographically as well as numerically.
        bool supportsRepetition =
            version.Length == 5 &&
            string.CompareOrdinal(version, FirstVersionWithRepetitionSeparator) >= 0;

        if (!supportsRepetition)
        {
            // 4010 and earlier: ISA11 is the Interchange Control Standards Identifier,
            // conventionally 'U' (U.S. standard). Not a delimiter.
            return null;
        }

        char candidate = isa11[0];

        // 'U' in a 5010 ISA11 is the legacy value carried over from 4010 and means
        // "repetition not used". Letters and digits cannot be delimiters in any case.
        if (candidate == 'U' || char.IsLetterOrDigit(candidate) || candidate == elementSeparator)
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Finds the index of ISA16, the component element separator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fast path is the fixed offset the specification guarantees. It is verified
    /// rather than trusted, because senders do get ISA field widths wrong — an ISA06 padded
    /// to 14 characters instead of 15 shifts everything after it and would otherwise make
    /// the parser read a data character as the segment terminator, producing a confusing
    /// failure hundreds of segments later.
    /// </para>
    /// <para>
    /// The fallback counts element separators instead. An ISA has 16 elements and therefore
    /// 16 separators; the character after the sixteenth separator is ISA16 regardless of
    /// whether the sender padded its fields correctly. This is strictly more tolerant, and
    /// safe, because a delimiter character can never appear inside element data.
    /// </para>
    /// </remarks>
    private static int LocateComponentSeparator(string input, int start, char element)
    {
        int fixedWidth = start + ComponentSeparatorOffset;

        // In a compliant ISA the character before ISA16, and the one after the terminator
        // position minus two, are both the element separator. Check the preceding one.
        if (fixedWidth < input.Length &&
            start + SegmentTerminatorOffset < input.Length &&
            input[fixedWidth - 1] == element)
        {
            return fixedWidth;
        }

        int separators = 0;
        int limit = Math.Min(input.Length, start + (IsaSegmentLength * 2));

        for (int i = start + ElementSeparatorOffset; i < limit; i++)
        {
            if (input[i] != element)
            {
                continue;
            }

            separators++;
            if (separators == IsaElementCount)
            {
                return i + 1;
            }
        }

        int available = input.Length - start;
        string lengthNote = available < IsaSegmentLength
            ? $" The input holds only {available} characters from the start of the ISA, " +
              $"shorter than the {IsaSegmentLength} characters of a complete ISA segment, so it is " +
              "most likely truncated."
            : string.Empty;

        throw new X12ParseException(
            "Could not locate ISA16 (the component element separator). Expected it at offset " +
            $"{ComponentSeparatorOffset} of the ISA segment, and found only {separators} of the " +
            $"{IsaElementCount} element separators an ISA must contain." + lengthNote)
        {
            SegmentPosition = 1,
        };
    }

    /// <summary>
    /// Finds the start of the ISA segment, tolerating a BOM and leading whitespace but
    /// nothing else — an interchange begins with ISA by definition, and searching further
    /// into the file risks matching the letters "ISA" inside somebody's data.
    /// </summary>
    private static int FindIsaStart(string input)
    {
        int i = 0;
        while (i < input.Length && (char.IsWhiteSpace(input[i]) || input[i] == '\uFEFF'))
        {
            i++;
        }

        if (i >= input.Length)
        {
            throw new X12ParseException("Input is empty. An X12 interchange begins with an ISA segment.");
        }

        // Identify before measuring. "Input begins with 'GS', not 'ISA'" is a far more
        // useful thing to be told than "input is too short", and a file that starts with
        // the wrong segment is usually a file that was split or concatenated wrongly.
        if (string.CompareOrdinal(input, i, "ISA", 0, Math.Min(3, input.Length - i)) != 0 ||
            input.Length - i < 3)
        {
            string head = input.Substring(i, Math.Min(3, input.Length - i));
            throw new X12ParseException(
                $"Input begins with '{head}', not 'ISA'. An X12 interchange begins with an ISA segment.");
        }

        // Only the element separator at offset 3 is needed to get started. The full
        // 106-character length is not required here on purpose: senders do send ISAs with
        // mis-padded fields, and LocateComponentSeparator can recover those. Truncation is
        // reported there, where the diagnosis is more precise.
        if (input.Length - i <= ElementSeparatorOffset)
        {
            throw new X12ParseException(
                $"Input is {input.Length - i} characters. An ISA segment is {IsaSegmentLength} " +
                "characters and this one is truncated before the data element separator at offset " +
                $"{ElementSeparatorOffset}.");
        }

        return i;
    }
}
