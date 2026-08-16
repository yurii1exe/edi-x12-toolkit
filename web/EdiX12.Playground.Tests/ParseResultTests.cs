using EdiX12.Core;
using EdiX12.Playground;
using EdiX12.Playground.Model;

namespace EdiX12.Playground.Tests;

/// <summary>
/// <see cref="ParseResult"/> is everything the page renders, computed once. These tests
/// pin the parts a reader would notice if they were wrong: that no segment is dropped or
/// duplicated, that the envelope roles land on the right rows, and that the rows a
/// diagnostic points at are the rows the table highlights.
/// </summary>
public class ParseResultTests
{
    private static ParseResult Parse(string fileName) =>
        ParseResult.From(X12Parser.Parse(SampleInterchanges.All.Single(s => s.FileName == fileName).Text));

    [Fact]
    public void Every_segment_gets_exactly_one_row_in_wire_order()
    {
        ParseResult r = Parse("214-shipment-status.edi");

        Assert.Equal(r.Interchange.Segments.Count, r.Rows.Count);
        Assert.Equal(
            r.Interchange.Segments.Select(s => s.Position),
            r.Rows.Select(row => row.Segment.Position));
    }

    [Fact]
    public void Envelope_segments_are_labelled_by_reference_not_by_position()
    {
        ParseResult r = Parse("214-shipment-status.edi");

        SegmentRow Row(string id) => r.Rows.Single(x => x.Segment.Id == id);

        Assert.Equal((0, "seg-env"), (Row("ISA").Depth, Row("ISA").Kind));
        Assert.Equal((0, "seg-env"), (Row("IEA").Depth, Row("IEA").Kind));
        Assert.Equal((1, "seg-group"), (Row("GS").Depth, Row("GS").Kind));
        Assert.Equal((1, "seg-group"), (Row("GE").Depth, Row("GE").Kind));
        Assert.Equal((2, "seg-txn"), (Row("ST").Depth, Row("ST").Kind));
        Assert.Equal((2, "seg-txn"), (Row("SE").Depth, Row("SE").Kind));
        Assert.Equal((3, "seg-body"), (Row("B10").Depth, Row("B10").Kind));
        Assert.Null(Row("B10").Note);
    }

    [Fact]
    public void Group_and_transaction_headers_carry_the_decoded_name()
    {
        ParseResult r = Parse("214-shipment-status.edi");

        Assert.Equal("group header · Transportation Carrier Shipment Status (214)",
            r.Rows.Single(x => x.Segment.Id == "GS").Note);
        Assert.Equal("transaction set · Transportation Carrier Shipment Status Message",
            r.Rows.Single(x => x.Segment.Id == "ST").Note);
    }

    [Fact]
    public void Counts_match_the_envelope()
    {
        ParseResult r = Parse("214-shipment-status.edi");

        Assert.Single(r.Interchange.Groups);
        Assert.Equal(1, r.TransactionCount);
        Assert.Equal(r.Interchange.Segments.Count, r.Rows.Count);
    }

    [Fact]
    public void A_sound_envelope_has_no_diagnostics_and_no_flagged_rows()
    {
        ParseResult r = Parse("214-shipment-status.edi");

        Assert.Empty(r.Diagnostics);
        Assert.Empty(r.FlaggedPositions);
    }

    [Fact]
    public void The_broken_sample_flags_exactly_the_segments_its_diagnostics_name()
    {
        ParseResult r = Parse("214-broken.edi");

        Assert.Equal(
            ["X12-SE01-COUNT", "X12-GE01-COUNT", "X12-IEA02-CONTROL"],
            r.Diagnostics.Select(d => d.Code));

        // SE, GE and IEA — the three trailers, which is what the highlight has to land on.
        Assert.Equal([9, 10, 11], r.FlaggedPositions.OrderBy(p => p));
        Assert.Equal(
            ["SE", "GE", "IEA"],
            r.Rows.Where(row => r.FlaggedPositions.Contains(row.Segment.Position)).Select(row => row.Segment.Id));
    }

    [Fact]
    public void Delimiters_are_the_ones_the_interchange_declared()
    {
        X12Delimiters conventional = Parse("214-shipment-status.edi").Delimiters;
        Assert.Equal('*', conventional.Element);
        Assert.Equal(':', conventional.Component);
        Assert.Equal('~', conventional.Segment);
        Assert.Equal('^', conventional.Repetition);

        X12Delimiters piped = Parse("214-pipe-delimited.edi").Delimiters;
        Assert.Equal('|', piped.Element);
        Assert.Equal('>', piped.Component);
        Assert.Equal('\n', piped.Segment);
        Assert.Equal('!', piped.Repetition);

        // 4010: ISA11 is the standards identifier 'U', so there is no repetition separator
        // to show, and the page prints "none" rather than a character.
        Assert.Null(Parse("214-4010.edi").Delimiters.Repetition);
    }

    [Fact]
    public void The_same_document_under_two_delimiter_sets_renders_the_same_rows()
    {
        ParseResult conventional = Parse("214-shipment-status.edi");
        ParseResult piped = Parse("214-pipe-delimited.edi");

        Assert.Equal(
            conventional.Rows.Select(r => (r.Segment.Id, r.Depth, r.Kind, r.Note)),
            piped.Rows.Select(r => (r.Segment.Id, r.Depth, r.Kind, r.Note)));
    }
}
