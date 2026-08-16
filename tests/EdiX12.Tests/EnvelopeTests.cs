using EdiX12.Core;

namespace EdiX12.Tests;

/// <summary>
/// Tests for the ISA / GS / ST envelope: what it decodes to, and what it reports when the
/// control numbers and counts do not add up.
/// </summary>
public class EnvelopeTests
{
    [Fact]
    public void ParsesInterchangeHeader()
    {
        Interchange interchange = X12Parser.Parse(Fixtures.Standard214OneLine);

        Assert.Equal("ZZ", interchange.SenderQualifier);
        Assert.Equal("DEMOSENDER", interchange.SenderId);
        Assert.Equal("ZZ", interchange.ReceiverQualifier);
        Assert.Equal("DEMORECEIVER", interchange.ReceiverId);
        Assert.Equal("00501", interchange.VersionNumber);
        Assert.Equal("000000001", interchange.ControlNumber);
        Assert.False(interchange.AcknowledgmentRequested);

        // ISA15 is 'T'. Anything that acts on a document should check this before it acts.
        Assert.Equal("T", interchange.UsageIndicator);
        Assert.False(interchange.IsProduction);
    }

    [Fact]
    public void ParsesInterchangeDateFromTwoDigitYear()
    {
        Interchange interchange = X12Parser.Parse(Fixtures.Standard214OneLine);

        // ISA09 is YYMMDD, not CCYYMMDD — the interchange header is less precise than the
        // GS04 underneath it, which carries a four-digit year.
        Assert.Equal(new DateTime(2026, 8, 15, 14, 30, 0), interchange.InterchangeDate);
    }

    [Fact]
    public void ParsesTheGroupAndTransactionTree()
    {
        Interchange interchange = X12Parser.Parse(Fixtures.Standard214OneLine);

        FunctionalGroup group = Assert.Single(interchange.Groups);
        Assert.Equal("QM", group.FunctionalIdentifierCode);
        Assert.Equal("005010", group.VersionReleaseIndustryCode);
        Assert.Equal("1", group.ControlNumber);

        TransactionSet transaction = Assert.Single(group.Transactions);
        Assert.Equal("214", transaction.IdentifierCode);
        Assert.Equal("0001", transaction.ControlNumber);

        // Body is everything between ST and SE, exclusive: B10, LX, AT7, MS1, MS2.
        Assert.Equal(
            new[] { "B10", "LX", "AT7", "MS1", "MS2" },
            transaction.Segments.Select(s => s.Id).ToArray());

        // What SE01 has to declare: the body plus ST and SE.
        Assert.Equal(7, transaction.DeclaredSegmentCount);
    }

    [Fact]
    public void ReadsShipmentIdentityFromB10()
    {
        TransactionSet transaction = X12Parser.Parse(Fixtures.Standard214OneLine).Transactions.Single();
        Segment b10 = transaction.Segments.Single(s => s.Id == "B10");

        Assert.Equal("SHIPMENT001", b10[2]);
        Assert.Equal("DEMO", b10[3]);
        Assert.Equal("B1003", b10.ElementReference(3));
    }

    [Fact]
    public void AWellFormedInterchangeProducesNoDiagnostics()
    {
        IReadOnlyList<X12Diagnostic> diagnostics = X12Parser.Parse(Fixtures.Standard214OneLine).Validate();

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void ReportsSegmentCountMismatch()
    {
        IReadOnlyList<X12Diagnostic> diagnostics =
            X12Parser.Parse(Fixtures.Standard214WrongSegmentCount).Validate();

        X12Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("X12-SE01-COUNT", diagnostic.Code);

        // The message has to name the element and both numbers. "Parse error" here costs
        // somebody an afternoon on a partner call.
        Assert.Contains("SE01", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'9'", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("7 segments", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInterchangeControlNumberThatDoesNotEchoBack()
    {
        IReadOnlyList<X12Diagnostic> diagnostics =
            X12Parser.Parse(Fixtures.Standard214WrongInterchangeControlNumber).Validate();

        X12Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("X12-IEA02-CONTROL", diagnostic.Code);
        Assert.Contains("000000002", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("000000001", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DelimitersDoNotChangeTheParsedDocument()
    {
        // The same 214 written with '*'/'~' and with '|'/newline must produce identical
        // objects. This is the test that a hard-coded parser cannot pass.
        Interchange conventional = X12Parser.Parse(Fixtures.Standard214OneLine);
        Interchange unconventional = X12Parser.Parse(Fixtures.Pipe214);

        Assert.Equal(conventional.ControlNumber, unconventional.ControlNumber);
        Assert.Equal(conventional.SenderId, unconventional.SenderId);
        Assert.Equal(conventional.Segments.Count, unconventional.Segments.Count);
        Assert.Empty(unconventional.Validate());

        // Everything after the ISA must be identical element for element. The ISA itself
        // is legitimately different: ISA11 and ISA16 hold the delimiter characters, so
        // they are the one place where two equivalent interchanges must differ.
        Assert.Equal(
            conventional.Segments.Skip(1).Select(Describe).ToArray(),
            unconventional.Segments.Skip(1).Select(Describe).ToArray());
    }

    [Fact]
    public void RejectsATransactionSetOutsideAFunctionalGroup()
    {
        string malformed = Fixtures.Standard214OneLine.Replace(
            "GS*QM*DEMOAPPSEND*DEMOAPPRECV*20260815*1430*1*X*005010~", string.Empty);

        var exception = Assert.Throws<X12ParseException>(() => X12Parser.Parse(malformed));

        Assert.Contains("outside any functional group", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, exception.SegmentPosition);
    }

    /// <summary>
    /// Renders a segment independently of the delimiters that produced it, so two
    /// differently-delimited interchanges can be compared element by element.
    /// </summary>
    private static string Describe(Segment segment) =>
        segment.Id + "[" + string.Join("][", segment.Elements) + "]";
}
