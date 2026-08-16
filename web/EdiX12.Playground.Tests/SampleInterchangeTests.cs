using EdiX12.Core;
using EdiX12.Playground;

namespace EdiX12.Playground.Tests;

/// <summary>
/// The one-click examples on the page are the repository's own sample files, embedded at
/// build time. These tests exist so that a sample which drifts, stops parsing, or fails to
/// embed fails the build instead of shipping a broken button.
/// </summary>
public class SampleInterchangeTests
{
    private static string SamplePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "samples", fileName);

    [Fact]
    public void All_four_repository_samples_are_offered()
    {
        string[] offered = SampleInterchanges.All.Select(s => s.FileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            ["214-4010.edi", "214-broken.edi", "214-pipe-delimited.edi", "214-shipment-status.edi"],
            offered);
    }

    [Theory]
    [InlineData("214-shipment-status.edi")]
    [InlineData("214-pipe-delimited.edi")]
    [InlineData("214-broken.edi")]
    [InlineData("214-4010.edi")]
    public void Embedded_text_is_byte_for_byte_the_file_in_samples(string fileName)
    {
        SampleInterchange sample = SampleInterchanges.All.Single(s => s.FileName == fileName);

        // Read with the same no-translation contract the embedder uses: the pipe-delimited
        // sample terminates its segments with a newline, so rewriting line endings here
        // would change what the file means.
        string onDisk = File.ReadAllText(SamplePath(fileName));

        Assert.Equal(onDisk, sample.Text);
    }

    [Fact]
    public void Every_sample_has_a_label_and_a_blurb()
    {
        foreach (SampleInterchange sample in SampleInterchanges.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(sample.Label), sample.FileName);
            Assert.False(string.IsNullOrWhiteSpace(sample.Blurb), sample.FileName);
        }
    }

    [Fact]
    public void Every_sample_parses_without_throwing()
    {
        foreach (SampleInterchange sample in SampleInterchanges.All)
        {
            Interchange interchange = X12Parser.Parse(sample.Text);
            Assert.NotEmpty(interchange.Segments);
        }
    }

    [Fact]
    public void The_first_sample_offered_is_the_broken_one()
    {
        // The page loads SampleInterchanges.All[0] on start and the empty state links to it
        // as "the broken sample", so the order is load-bearing, not cosmetic.
        Assert.Equal("214-broken.edi", SampleInterchanges.All[0].FileName);
        Assert.NotEmpty(X12Parser.Parse(SampleInterchanges.All[0].Text).Validate());
    }

    [Fact]
    public void Only_the_broken_sample_produces_diagnostics()
    {
        foreach (SampleInterchange sample in SampleInterchanges.All.Where(s => s.FileName != "214-broken.edi"))
        {
            Assert.Empty(X12Parser.Parse(sample.Text).Validate());
        }
    }
}
