namespace EdiX12.Core;

/// <summary>
/// Entry point: raw X12 text in, typed <see cref="Interchange"/> out.
/// </summary>
public static class X12Parser
{
    /// <summary>
    /// Parses one interchange.
    /// </summary>
    /// <param name="input">
    /// The full text of an interchange, beginning with ISA. Line endings between segments
    /// are optional and either style is accepted.
    /// </param>
    /// <returns>The parsed interchange.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
    /// <exception cref="X12ParseException">
    /// The ISA cannot be read, or the envelope segments are nested in an order that has no
    /// meaning — a GS inside a GS, an SE with no ST. Missing trailers are tolerated and
    /// reported by <see cref="Interchange.Validate"/> instead, so that a defective file can
    /// still be inspected.
    /// </exception>
    public static Interchange Parse(string input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        IReadOnlyList<Segment> segments = X12Tokenizer.Tokenize(input, out X12Delimiters delimiters);
        return Build(segments, delimiters);
    }

    /// <summary>
    /// Assembles a flat segment list into the ISA / GS / ST tree.
    /// </summary>
    private static Interchange Build(IReadOnlyList<Segment> segments, X12Delimiters delimiters)
    {
        Segment isa = segments[0];
        Segment? iea = null;

        var groups = new List<FunctionalGroup>();

        Segment? currentGs = null;
        var currentTransactions = new List<TransactionSet>();

        Segment? currentSt = null;
        var currentBody = new List<Segment>();

        for (int i = 1; i < segments.Count; i++)
        {
            Segment segment = segments[i];

            switch (segment.Id)
            {
                case "GS":
                    if (currentGs is not null)
                    {
                        throw Structural(
                            "GS segment opens a functional group while group " +
                            $"'{currentGs[6].Trim()}' is still open. The previous group is missing its GE.",
                            segment);
                    }

                    currentGs = segment;
                    currentTransactions = new List<TransactionSet>();
                    break;

                case "GE":
                    if (currentGs is null)
                    {
                        throw Structural("GE segment closes a functional group that was never opened by a GS.", segment);
                    }

                    if (currentSt is not null)
                    {
                        throw Structural(
                            $"GE segment closes the group while transaction set '{currentSt[2].Trim()}' " +
                            "is still open. The transaction set is missing its SE.",
                            segment);
                    }

                    groups.Add(new FunctionalGroup(currentGs, segment, currentTransactions));
                    currentGs = null;
                    break;

                case "ST":
                    if (currentGs is null)
                    {
                        throw Structural("ST segment starts a transaction set outside any functional group.", segment);
                    }

                    if (currentSt is not null)
                    {
                        throw Structural(
                            $"ST segment starts a transaction set while '{currentSt[2].Trim()}' is " +
                            "still open. The previous transaction set is missing its SE.",
                            segment);
                    }

                    currentSt = segment;
                    currentBody = new List<Segment>();
                    break;

                case "SE":
                    if (currentSt is null)
                    {
                        throw Structural("SE segment closes a transaction set that was never opened by an ST.", segment);
                    }

                    currentTransactions.Add(new TransactionSet(currentSt, segment, currentBody));
                    currentSt = null;
                    break;

                case "IEA":
                    iea = segment;
                    break;

                default:
                    if (currentSt is not null)
                    {
                        currentBody.Add(segment);
                    }

                    // Segments outside a transaction set are either TA1 acknowledgments or
                    // sender defects. They stay reachable through Interchange.Segments
                    // rather than being dropped.
                    break;
            }
        }

        // Unclosed structures are kept rather than rejected, so that a partner's broken
        // file can still be read and diagnosed. Validate() reports what is missing.
        if (currentSt is not null)
        {
            currentTransactions.Add(new TransactionSet(currentSt, null, currentBody));
        }

        if (currentGs is not null)
        {
            groups.Add(new FunctionalGroup(currentGs, null, currentTransactions));
        }

        return new Interchange(isa, iea, groups, segments, delimiters);
    }

    private static X12ParseException Structural(string message, Segment segment) =>
        new X12ParseException($"{message} (segment {segment.Position}: {segment.Id})")
        {
            SegmentPosition = segment.Position,
        };
}
