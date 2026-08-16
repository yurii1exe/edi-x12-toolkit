using EdiX12.Core;

namespace EdiX12.Tests;

/// <summary>
/// Tests for the part everyone gets wrong: finding out what the delimiters are before
/// trying to split anything with them.
/// </summary>
public class TokenizerTests
{
    [Theory]
    [InlineData(Fixtures.IsaStandard)]
    [InlineData(Fixtures.IsaPipeNewline)]
    [InlineData(Fixtures.Isa4010)]
    public void CompliantIsaIsExactly106Characters(string isa)
    {
        // Guards the fixtures themselves. The whole fixed-offset scheme depends on this
        // length, so a typo in a fixture must fail here rather than as a confusing
        // delimiter error three tests later.
        Assert.Equal(X12Tokenizer.IsaSegmentLength, isa.Length);
    }

    [Fact]
    public void ReadsConventionalDelimitersFromIsa()
    {
        X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(Fixtures.Standard214OneLine);

        Assert.Equal('*', delimiters.Element);
        Assert.Equal(':', delimiters.Component);
        Assert.Equal('~', delimiters.Segment);
        Assert.Equal('^', delimiters.Repetition);
    }

    [Fact]
    public void ReadsUnconventionalDelimitersFromIsa()
    {
        // Nothing here is '*' or '~', and the segment terminator is a newline. A parser
        // that hard-codes the conventional delimiters, or that strips line endings before
        // tokenising, fails on this input.
        X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(Fixtures.Pipe214);

        Assert.Equal('|', delimiters.Element);
        Assert.Equal('>', delimiters.Component);
        Assert.Equal('\n', delimiters.Segment);
        Assert.Equal('!', delimiters.Repetition);
    }

    [Fact]
    public void Isa11IsNotARepetitionSeparatorBefore5010()
    {
        // In 4010 ISA11 is the Interchange Control Standards Identifier, conventionally
        // 'U'. Treating it as a delimiter would split element values on the letter U.
        X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(Fixtures.Isa4010);

        Assert.Null(delimiters.Repetition);
        Assert.Equal('*', delimiters.Element);
        Assert.Equal(':', delimiters.Component);
        Assert.Equal('~', delimiters.Segment);
    }

    [Fact]
    public void RecoversDelimitersWhenSenderGetsIsaFieldWidthsWrong()
    {
        // ISA06 is one character short, so offset 104 is no longer ISA16. Falling back to
        // counting element separators finds it anyway: an ISA has exactly 16 of them, and
        // a delimiter can never appear inside element data.
        X12Delimiters delimiters = X12Tokenizer.ReadDelimiters(Fixtures.IsaNarrowSenderId);

        Assert.Equal('*', delimiters.Element);
        Assert.Equal(':', delimiters.Component);
        Assert.Equal('~', delimiters.Segment);
    }

    [Fact]
    public void LineEndingsBetweenSegmentsDoNotChangeTheResult()
    {
        string[] oneLine = Render(X12Tokenizer.Tokenize(Fixtures.Standard214OneLine));
        string[] crlf = Render(X12Tokenizer.Tokenize(Fixtures.Standard214CrLf));
        string[] lf = Render(X12Tokenizer.Tokenize(Fixtures.Standard214Lf));

        Assert.Equal(oneLine, crlf);
        Assert.Equal(oneLine, lf);
    }

    [Fact]
    public void TokenizesIsaWithoutTrimmingFixedWidthPadding()
    {
        Segment isa = X12Tokenizer.Tokenize(Fixtures.Standard214OneLine)[0];

        Assert.Equal("ISA", isa.Id);
        Assert.Equal(16, isa.ElementCount);
        Assert.Equal(1, isa.Position);

        // ISA06 is space-padded to 15 on the wire. The tokenizer preserves it exactly;
        // trimming is the typed layer's decision, not the tokenizer's.
        Assert.Equal("DEMOSENDER     ", isa[6]);
        Assert.Equal("ISA06", isa.ElementReference(6));
    }

    [Fact]
    public void SegmentPositionsAreSequentialAndOneBased()
    {
        IReadOnlyList<Segment> segments = X12Tokenizer.Tokenize(Fixtures.Standard214CrLf);

        Assert.Equal(11, segments.Count);
        Assert.Equal("ISA", segments[0].Id);
        Assert.Equal("IEA", segments[10].Id);

        for (int i = 0; i < segments.Count; i++)
        {
            Assert.Equal(i + 1, segments[i].Position);
        }
    }

    [Fact]
    public void EmptyElementsArePreservedInPosition()
    {
        // "AT7*AF*NS***20260815*1430*LT" — AT703 and AT704 are absent. If a tokenizer
        // collapses empty elements, every element after them shifts and the date lands in
        // the wrong field.
        Segment at7 = Assert.Single(
            X12Tokenizer.Tokenize(Fixtures.Standard214OneLine), s => s.Id == "AT7");

        Assert.Equal("AF", at7[1]);
        Assert.Equal("NS", at7[2]);
        Assert.Equal(string.Empty, at7[3]);
        Assert.Equal(string.Empty, at7[4]);
        Assert.Equal("20260815", at7[5]);
        Assert.Equal("1430", at7[6]);
        Assert.Equal("LT", at7[7]);

        // Elements past the end of the segment read as empty, because in X12 "absent" and
        // "empty" are the same thing.
        Assert.Equal(string.Empty, at7[8]);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("GS*QM*A*B*20260815*1430*1*X*005010~", "not 'ISA'")]
    public void RejectsInputThatDoesNotStartWithAnIsa(string input, string expectedFragment)
    {
        var exception = Assert.Throws<X12ParseException>(() => X12Tokenizer.Tokenize(input));

        Assert.Contains(expectedFragment, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsIsaThatIsTooShortToContainDelimiters()
    {
        var exception = Assert.Throws<X12ParseException>(() => X12Tokenizer.Tokenize("ISA*00*"));

        Assert.Contains("shorter than the 106 characters", exception.Message, StringComparison.Ordinal);
    }

    private static string[] Render(IReadOnlyList<Segment> segments) =>
        segments.Select(s => s.ToString()).ToArray();
}
