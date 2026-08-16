using System.Text.RegularExpressions;

namespace EdiX12.Playground.Tests;

/// <summary>
/// The page tells its visitor that their file never leaves the browser. A stylesheet, font
/// or script pulled from another host would leak the fact of the visit even if it could not
/// leak the file, and would make the sentence on the page not quite true. This asserts the
/// shell and the stylesheet load nothing off-host.
/// </summary>
/// <remarks>
/// Anchors are exempt on purpose: <c>&lt;a href&gt;</c> is a link the visitor chooses to
/// follow, not a resource the browser fetches on load. Only tags that cause a fetch are
/// checked.
/// </remarks>
public class NoExternalResourceTests
{
    private static string Read(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "site", fileName));

    [Fact]
    public void The_page_shell_fetches_nothing_from_another_host()
    {
        string html = Read("index.html");

        // Every attribute the browser resolves and fetches without being asked.
        MatchCollection fetched = Regex.Matches(
            html,
            @"<(?:link|script|img|iframe|source|audio|video|embed|object)\b[^>]*?\b(?:src|href|data)\s*=\s*""([^""]*)""",
            RegexOptions.IgnoreCase);

        Assert.NotEmpty(fetched);
        foreach (Match match in fetched)
        {
            string url = match.Groups[1].Value;
            Assert.False(
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("//", StringComparison.Ordinal),
                $"index.html fetches {url} from another host");
        }
    }

    [Fact]
    public void The_stylesheet_imports_and_urls_are_all_local()
    {
        string css = Read("app.css");

        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(css, @"url\(\s*['""]?([^'"")]+)", RegexOptions.IgnoreCase))
        {
            string url = match.Groups[1].Value.Trim();
            Assert.False(
                url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("//", StringComparison.Ordinal),
                $"app.css loads {url} from another host");
        }
    }

    [Fact]
    public void The_shell_keeps_the_root_base_href_the_pages_workflow_rewrites()
    {
        // .github/workflows/pages.yml greps for this exact string before rewriting it to
        // the project-site path. If it is reformatted here, the deploy fails loudly rather
        // than shipping a site whose assets all 404 — but only if this stays in step.
        Assert.Contains("""<base href="/" />""", Read("index.html"), StringComparison.Ordinal);
    }
}
