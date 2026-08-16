using EdiX12.Core;

namespace EdiX12.Playground.Model;

/// <summary>
/// One segment as the page renders it: the segment itself plus where it sits in the envelope.
/// </summary>
/// <param name="Segment">The parsed segment.</param>
/// <param name="Depth">Nesting depth — 0 interchange, 1 group, 2 transaction set, 3 body.</param>
/// <param name="Kind">CSS class naming the role, e.g. <c>seg-isa</c>.</param>
/// <param name="Note">Short role label shown beside the identifier, or <see langword="null"/>.</param>
public sealed record SegmentRow(Segment Segment, int Depth, string Kind, string? Note);

/// <summary>
/// Everything the page needs from one parse, computed once rather than during rendering.
/// </summary>
public sealed class ParseResult
{
    private ParseResult(
        Interchange interchange,
        IReadOnlyList<X12Diagnostic> diagnostics,
        IReadOnlyList<SegmentRow> rows,
        IReadOnlySet<int> flaggedPositions,
        int transactionCount)
    {
        Interchange = interchange;
        Diagnostics = diagnostics;
        Rows = rows;
        FlaggedPositions = flaggedPositions;
        TransactionCount = transactionCount;
    }

    /// <summary>The parsed interchange.</summary>
    public Interchange Interchange { get; }

    /// <summary>The delimiters it declared in its ISA.</summary>
    public X12Delimiters Delimiters => Interchange.Delimiters;

    /// <summary>
    /// Envelope diagnostics in segment order, matching what <c>edix12 validate</c> prints.
    /// <see cref="Interchange.Validate"/> returns them outermost first — IEA, then GE, then
    /// SE — which reads backwards next to a segment table.
    /// </summary>
    public IReadOnlyList<X12Diagnostic> Diagnostics { get; }

    /// <summary>Every segment in wire order, annotated with its envelope role.</summary>
    public IReadOnlyList<SegmentRow> Rows { get; }

    /// <summary>Segment positions a diagnostic points at, so the table can highlight them.</summary>
    public IReadOnlySet<int> FlaggedPositions { get; }

    /// <summary>Transaction sets across all groups.</summary>
    public int TransactionCount { get; }

    /// <summary>Builds the render model for one interchange.</summary>
    public static ParseResult From(Interchange interchange)
    {
        // Roles are established by reference: the envelope objects expose the very same
        // Segment instances that appear in Interchange.Segments, so no positional guessing
        // is needed and a malformed file cannot shift the labels.
        var roles = new Dictionary<Segment, (int Depth, string Kind, string? Note)>();

        roles[interchange.Header] = (0, "seg-env", "interchange header");
        if (interchange.Trailer is { } iea)
        {
            roles[iea] = (0, "seg-env", "interchange trailer");
        }

        int transactionCount = 0;

        foreach (FunctionalGroup group in interchange.Groups)
        {
            string groupNote = X12Reference.FunctionalIdentifier(group.FunctionalIdentifierCode) is { } fn
                ? $"group header · {fn}"
                : "group header";

            roles[group.Header] = (1, "seg-group", groupNote);
            if (group.Trailer is { } ge)
            {
                roles[ge] = (1, "seg-group", "group trailer");
            }

            foreach (TransactionSet transaction in group.Transactions)
            {
                transactionCount++;

                string stNote = X12Reference.TransactionSetName(transaction.IdentifierCode) is { } tn
                    ? $"transaction set · {tn}"
                    : "transaction set";

                roles[transaction.Header] = (2, "seg-txn", stNote);
                if (transaction.Trailer is { } se)
                {
                    roles[se] = (2, "seg-txn", "transaction set trailer");
                }
            }
        }

        var rows = new List<SegmentRow>(interchange.Segments.Count);
        foreach (Segment segment in interchange.Segments)
        {
            if (roles.TryGetValue(segment, out (int Depth, string Kind, string? Note) role))
            {
                rows.Add(new SegmentRow(segment, role.Depth, role.Kind, role.Note));
            }
            else
            {
                rows.Add(new SegmentRow(segment, 3, "seg-body", null));
            }
        }

        // Sorted by segment position so the table reads in wire order, the same ordering
        // CliRunner applies before printing. OrderBy is stable, so two diagnostics on one
        // segment keep the order Validate() produced them in.
        IReadOnlyList<X12Diagnostic> diagnostics = interchange.Validate()
            .OrderBy(d => d.SegmentPosition)
            .ToArray();
        var flagged = new HashSet<int>(diagnostics.Select(d => d.SegmentPosition));

        return new ParseResult(interchange, diagnostics, rows, flagged, transactionCount);
    }
}
