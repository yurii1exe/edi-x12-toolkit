using Bunit;
using EdiX12.Playground;

// `Playground` alone binds to the EdiX12.Playground namespace from inside
// EdiX12.Playground.Tests, so the component needs an alias.
using PlaygroundPage = EdiX12.Playground.Pages.Playground;

namespace EdiX12.Playground.Tests;

/// <summary>
/// Renders the page component into a DOM and asserts what a visitor actually sees. These
/// are here because the markup is where a correct parse can still be presented wrongly —
/// a diagnostic that renders without its element reference, or a segment table that drops
/// a row, is invisible to the model tests.
/// </summary>
public class PlaygroundPageTests : BunitContext
{
    private IRenderedComponent<PlaygroundPage> RenderPage() => Render<PlaygroundPage>();

    [Fact]
    public void The_page_opens_on_the_broken_sample_and_shows_its_three_diagnostics()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();

        string[] codes = page.FindAll("table.tbl-diag tbody tr td:first-child code")
            .Select(e => e.TextContent.Trim())
            .ToArray();

        Assert.Equal(["X12-SE01-COUNT", "X12-GE01-COUNT", "X12-IEA02-CONTROL"], codes);
    }

    [Fact]
    public void The_SE01_row_names_the_element_and_the_segment_it_sits_on()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();

        AngleSharp.Dom.IElement row = page.FindAll("table.tbl-diag tbody tr")[0];
        string[] cells = row.QuerySelectorAll("td").Select(c => c.TextContent.Trim()).ToArray();

        Assert.Equal("X12-SE01-COUNT", cells[0]);
        Assert.Equal("9", cells[1]);
        Assert.Contains("SE01", cells[2]);
        Assert.Contains("Number of Included Segments", cells[2]);
        Assert.Contains("declares '9'", cells[3]);
        Assert.Contains("contains 7 segments", cells[3]);
    }

    [Fact]
    public void The_segments_the_diagnostics_name_are_the_rows_marked_flagged()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();

        string[] flagged = page.FindAll("table.tbl-seg tr.flagged .id")
            .Select(e => e.TextContent.Trim())
            .ToArray();

        Assert.Equal(["SE", "GE", "IEA"], flagged);
    }

    [Fact]
    public void Every_segment_in_the_interchange_gets_a_row()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();

        int rows = page.FindAll("table.tbl-seg tbody tr").Count;
        Assert.Equal(11, rows);
    }

    [Fact]
    public void A_sound_envelope_reports_no_diagnostics_instead_of_an_empty_table()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        page.Find("textarea.editor").Input(Sample("214-shipment-status.edi"));

        Assert.Empty(page.FindAll("table.tbl-diag"));
        Assert.Contains("9 envelope checks passed", page.Markup);
    }

    [Fact]
    public void The_pipe_delimited_sample_reports_the_delimiters_it_declared()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        page.Find("textarea.editor").Input(Sample("214-pipe-delimited.edi"));

        string[] glyphs = page.FindAll("table.tbl-delim tbody .glyph")
            .Select(e => e.TextContent.Trim())
            .ToArray();

        // The newline terminator is shown as an escape, not as the word "newline", and not
        // as an invisible gap in the table.
        Assert.Equal(["|", ">", @"\n", "!"], glyphs);
    }

    [Fact]
    public void A_4010_interchange_says_ISA11_is_not_a_delimiter_rather_than_showing_U()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        page.Find("textarea.editor").Input(Sample("214-4010.edi"));

        AngleSharp.Dom.IElement repetitionRow = page.FindAll("table.tbl-delim tbody tr")[3];
        Assert.Equal("none", repetitionRow.QuerySelector(".glyph")!.TextContent.Trim());
        Assert.Contains("ISA11 is not a delimiter at version 00401", repetitionRow.TextContent);
        Assert.DoesNotContain("U", repetitionRow.QuerySelector(".glyph")!.TextContent);
    }

    [Fact]
    public void Element_names_from_the_specification_appear_as_tooltips_on_the_segment_table()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        page.Find("textarea.editor").Input(Sample("214-shipment-status.edi"));

        string markup = page.Markup;
        Assert.Contains("ISA16 — Component Element Separator", markup);
        Assert.Contains("B1002 — Shipment Identification Number", markup);
    }

    [Fact]
    public void Text_that_is_not_an_interchange_produces_the_libraries_explanation_not_a_stack_trace()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        page.Find("textarea.editor").Input("this is not an interchange");

        Assert.Empty(page.FindAll("table.tbl-seg"));
        AngleSharp.Dom.IElement fatal = page.Find(".card-fatal");
        Assert.Contains("Cannot parse", fatal.TextContent);
        Assert.DoesNotContain("at EdiX12.Core", fatal.TextContent);
    }

    [Fact]
    public void Clearing_the_editor_empties_the_page_rather_than_leaving_a_stale_parse()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        Assert.NotEmpty(page.FindAll("table.tbl-seg"));

        page.FindAll("button").Single(b => b.TextContent.Trim() == "Clear").Click();

        Assert.Empty(page.FindAll("table.tbl-seg"));
        Assert.Contains("Nothing parsed yet", page.Markup);
    }

    [Fact]
    public void Each_sample_has_its_own_one_click_button()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();

        string[] labels = page.FindAll(".samples button.chip").Select(b => b.TextContent.Trim()).ToArray();
        Assert.Equal(SampleInterchanges.All.Select(s => s.Label), labels);

        // Loading one replaces the editor contents with that file.
        page.FindAll(".samples button.chip")[1].Click();
        Assert.Contains("214-shipment-status.edi", page.Find(".sample-blurb").TextContent);
    }

    [Fact]
    public void The_page_states_that_parsing_happens_locally()
    {
        IRenderedComponent<PlaygroundPage> page = RenderPage();
        Assert.Contains("Your file never leaves this browser", page.Find(".privacy").TextContent);
    }

    private static string Sample(string fileName) =>
        SampleInterchanges.All.Single(s => s.FileName == fileName).Text;
}
