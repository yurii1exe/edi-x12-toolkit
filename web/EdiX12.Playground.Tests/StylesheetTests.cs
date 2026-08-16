namespace EdiX12.Playground.Tests;

/// <summary>
/// The phone layout, asserted against the stylesheet the site ships rather than against a
/// screenshot. A diagnostics row carries the longest strings on the page — a code, an
/// element name and a full sentence — and the card it sits in clips what does not fit, so
/// the rules that let the row stack are load-bearing rather than cosmetic.
/// </summary>
public class StylesheetTests
{
    private static string Css() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "site", "app.css"));

    /// <summary>The body of the media query the phone layout lives in.</summary>
    private static string PhoneLayout()
    {
        string css = Css();
        int at = css.IndexOf("@media (max-width: 900px)", StringComparison.Ordinal);
        Assert.True(at >= 0, "app.css no longer has a (max-width: 900px) media query.");

        int open = css.IndexOf('{', at);
        int depth = 0;

        for (int i = open; i < css.Length; i++)
        {
            if (css[i] == '{') { depth++; }
            else if (css[i] == '}' && --depth == 0) { return css[(open + 1)..i]; }
        }

        throw new InvalidOperationException("The media query is never closed.");
    }

    [Fact]
    public void The_diagnostic_code_may_wrap_on_a_phone()
    {
        // X12-IEA02-CONTROL is the widest unbreakable string the page can produce. Held on
        // one line it sets the table's minimum width, and that minimum is what the card
        // has to clip. Measured in Chrome at a 390px viewport against 356px of card: the
        // table needs 356.27px with this rule, 122.56px without it.
        string phone = PhoneLayout();

        Assert.Matches(@"\.code-bad\s*\{[^}]*white-space:\s*normal", phone);
        Assert.Matches(@"\.code-bad\s*\{[^}]*overflow-wrap:\s*anywhere", phone);
    }

    [Fact]
    public void A_diagnostic_stacks_into_one_column_on_a_phone()
    {
        // Four columns across a phone leave the message column a quarter of the card.
        // Stacked, it gets all of it.
        string phone = PhoneLayout();

        Assert.Matches(@"\.tbl-diag tbody tr\s*\{[^}]*display:\s*grid", phone);
        Assert.Matches(@"\.tbl-diag thead\s*\{[^}]*display:\s*none", phone);
    }

    [Fact]
    public void The_fixed_diagnostic_column_widths_are_released_on_a_phone()
    {
        // .tbl-diag td:nth-child(n) is a more specific selector than .tbl-diag tbody td,
        // so a stacking rule written without the :nth-child loses to the desktop widths
        // and the grid tracks silently stay at the desktop numbers.
        string phone = PhoneLayout();

        foreach (int column in new[] { 1, 2, 3 })
        {
            Assert.Matches(@$"\.tbl-diag td:nth-child\({column}\)[^{{]*\{{[^}}]*width:\s*auto", phone);
        }
    }
}
