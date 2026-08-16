using EdiX12.Core;
using EdiX12.Playground;

namespace EdiX12.Playground.Tests;

/// <summary>
/// <see cref="X12Reference"/> is display labelling only. The rule it has to keep is that it
/// never guesses: an element or code it does not know returns <see langword="null"/> so the
/// page can fall back to the bare specification reference.
/// </summary>
public class X12ReferenceTests
{
    [Theory]
    [InlineData("ISA", 16, "Component Element Separator")]
    [InlineData("ISA", 13, "Interchange Control Number")]
    [InlineData("SE", 1, "Number of Included Segments")]
    [InlineData("GE", 1, "Number of Transaction Sets Included")]
    [InlineData("IEA", 2, "Interchange Control Number")]
    [InlineData("B10", 2, "Shipment Identification Number")]
    public void Known_elements_are_named(string segmentId, int element, string expected) =>
        Assert.Equal(expected, X12Reference.ElementName(segmentId, element));

    [Theory]
    [InlineData("ISA", 0)]     // element numbers are 1-based
    [InlineData("ISA", 17)]    // an ISA has 16 elements
    [InlineData("SE", 3)]
    [InlineData("ZZZ", 1)]     // segment not in the table
    [InlineData("isa", 1)]     // the table is ordinal; case is not a match
    public void Unknown_elements_are_null_rather_than_guessed(string segmentId, int element) =>
        Assert.Null(X12Reference.ElementName(segmentId, element));

    [Fact]
    public void Qualifier_functional_and_transaction_lookups_miss_cleanly()
    {
        Assert.Equal("SCAC", X12Reference.IdQualifier("02"));
        Assert.Null(X12Reference.IdQualifier("99"));

        Assert.Equal("Transportation Carrier Shipment Status (214)", X12Reference.FunctionalIdentifier("QM"));
        Assert.Null(X12Reference.FunctionalIdentifier("XX"));

        Assert.Equal("Transportation Carrier Shipment Status Message", X12Reference.TransactionSetName("214"));
        Assert.Null(X12Reference.TransactionSetName("000"));
    }

    /// <summary>
    /// The nine codes <see cref="Interchange.Validate"/> documents. If the library grows a
    /// tenth, this list and the table it drives have to grow with it.
    /// </summary>
    private static readonly string[] DocumentedCodes =
    [
        "X12-IEA-MISSING", "X12-IEA01-COUNT", "X12-IEA02-CONTROL",
        "X12-GE-MISSING", "X12-GE01-COUNT", "X12-GE02-CONTROL",
        "X12-SE-MISSING", "X12-SE01-COUNT", "X12-SE02-CONTROL",
    ];

    [Fact]
    public void Every_documented_diagnostic_code_has_an_element_label()
    {
        foreach (string code in DocumentedCodes)
        {
            (string? reference, string? description) = X12Reference.DiagnosticElement(code);
            Assert.False(string.IsNullOrWhiteSpace(reference), code);
            Assert.False(string.IsNullOrWhiteSpace(description), code);

            // The reference must be the element the code is named after, so the column is
            // not just restating the code.
            Assert.StartsWith(reference!, code["X12-".Length..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_code_the_samples_actually_emit_has_a_label()
    {
        // Drift guard against the library: whatever Validate() produces for the files this
        // page ships must render with a named element rather than an em dash.
        foreach (SampleInterchange sample in SampleInterchanges.All)
        {
            foreach (X12Diagnostic diagnostic in X12Parser.Parse(sample.Text).Validate())
            {
                Assert.NotNull(X12Reference.DiagnosticElement(diagnostic.Code).Reference);
            }
        }
    }

    [Fact]
    public void An_unknown_diagnostic_code_yields_no_label_rather_than_a_wrong_one()
    {
        (string? reference, string? description) = X12Reference.DiagnosticElement("X12-SOMETHING-NEW");
        Assert.Null(reference);
        Assert.Null(description);
    }
}
