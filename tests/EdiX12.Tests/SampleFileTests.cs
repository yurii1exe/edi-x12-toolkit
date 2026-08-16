using EdiX12.Core;

namespace EdiX12.Tests;

/// <summary>
/// Parses the files in <c>samples/</c>, so that the examples the README points a reader at
/// cannot rot without failing the build.
/// </summary>
public class SampleFileTests
{
    private static readonly string SamplesDirectory =
        Path.Combine(AppContext.BaseDirectory, "samples");

    [Theory]
    [InlineData("214-shipment-status.edi")]
    [InlineData("214-pipe-delimited.edi")]
    [InlineData("214-4010.edi")]
    public void SampleParsesAndValidatesCleanly(string fileName)
    {
        Interchange interchange = X12Parser.Parse(ReadSample(fileName));

        TransactionSet transaction = Assert.Single(interchange.Transactions);
        Assert.Equal("214", transaction.IdentifierCode);
        Assert.Empty(interchange.Validate());
    }

    [Fact]
    public void TheTwoSamplesAreTheSameDocumentWithDifferentDelimiters()
    {
        // 214-shipment-status.edi uses '*' and '~'. 214-pipe-delimited.edi uses '|' and a
        // newline. Same document, and this is the claim the README makes.
        Interchange conventional = X12Parser.Parse(ReadSample("214-shipment-status.edi"));
        Interchange unconventional = X12Parser.Parse(ReadSample("214-pipe-delimited.edi"));

        Assert.Equal('*', conventional.Delimiters.Element);
        Assert.Equal('~', conventional.Delimiters.Segment);
        Assert.Equal('|', unconventional.Delimiters.Element);
        Assert.Equal('\n', unconventional.Delimiters.Segment);

        Assert.Equal(
            conventional.Segments.Skip(1).Select(Describe).ToArray(),
            unconventional.Segments.Skip(1).Select(Describe).ToArray());
    }

    [Fact]
    public void The4010SampleDeclaresNoRepetitionSeparator()
    {
        // ISA11 is 'U' here — the Interchange Control Standards Identifier, not a
        // delimiter. Reading it as one splits element values on the letter U.
        Interchange interchange = X12Parser.Parse(ReadSample("214-4010.edi"));

        Assert.Equal("00401", interchange.VersionNumber);
        Assert.Null(interchange.Delimiters.Repetition);
    }

    [Fact]
    public void TheBrokenSampleFailsExactlyTheThreeChecksItIsMeantTo()
    {
        // samples/214-broken.edi is the input the README's validate example uses, and the
        // one the CLI demonstration reads. If it ever stops being broken in precisely this
        // way, the documented output is wrong.
        Interchange interchange = X12Parser.Parse(ReadSample("214-broken.edi"));

        Assert.Equal(
            new[] { "X12-IEA02-CONTROL", "X12-GE01-COUNT", "X12-SE01-COUNT" },
            interchange.Validate().Select(d => d.Code).ToArray());
    }

    private static string ReadSample(string fileName)
    {
        string path = Path.Combine(SamplesDirectory, fileName);
        Assert.True(File.Exists(path), $"Sample file not found: {path}");
        return File.ReadAllText(path);
    }

    private static string Describe(Segment segment) =>
        segment.Id + "[" + string.Join("][", segment.Elements) + "]";
}
