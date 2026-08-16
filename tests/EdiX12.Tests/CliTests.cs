using System.Text.Json;
using EdiX12.Cli;

namespace EdiX12.Tests;

/// <summary>
/// Drives the <c>edix12</c> tool through <see cref="CliRunner.Run"/> with in-memory streams.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here spawns a process. The CLI takes its three streams, its redirection state
/// and its terminal width as arguments precisely so that the argument handling, the exit
/// codes and the rendered output are all reachable from a unit test — a tool whose only
/// test is "it did not crash when I ran it by hand" is a tool that breaks silently.
/// </para>
/// <para>
/// The exit codes are the contract that matters most: <c>validate</c> returning 1 is what
/// makes the tool usable as a CI step, and it is asserted here rather than assumed.
/// </para>
/// </remarks>
public class CliTests
{
    private static readonly string SamplesDirectory =
        Path.Combine(AppContext.BaseDirectory, "samples");

    private static string Sample(string fileName) => Path.Combine(SamplesDirectory, fileName);

    // -- parse ------------------------------------------------------------------------

    [Fact]
    public void ParseWritesTheEnvelopeAsJson()
    {
        CliResult result = Run("parse", Sample("214-shipment-status.edi"));

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Empty(result.Error);

        JsonElement root = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("DEMOSENDER", root.GetProperty("interchange").GetProperty("senderId").GetString());
        Assert.Equal("DEMORECEIVER", root.GetProperty("interchange").GetProperty("receiverId").GetString());
        Assert.False(root.GetProperty("interchange").GetProperty("isProduction").GetBoolean());
        Assert.Empty(root.GetProperty("diagnostics").EnumerateArray());
    }

    [Fact]
    public void ParseCarriesTheTransactionBodyDownToElements()
    {
        CliResult result = Run("parse", Sample("214-shipment-status.edi"));

        JsonElement transaction = JsonDocument.Parse(result.Output).RootElement
            .GetProperty("interchange").GetProperty("groups")[0]
            .GetProperty("transactions")[0];

        Assert.Equal("214", transaction.GetProperty("identifierCode").GetString());
        Assert.Equal(7, transaction.GetProperty("declaredSegmentCount").GetInt32());

        JsonElement b10 = transaction.GetProperty("segments")[0];
        Assert.Equal("B10", b10.GetProperty("id").GetString());
        Assert.Equal("SHIPMENT001", b10.GetProperty("elements")[1].GetString());
    }

    [Fact]
    public void ParseIsCompactByDefaultAndIndentedWithPretty()
    {
        string compact = Run("parse", Sample("214-shipment-status.edi")).Output;
        string pretty = Run("parse", Sample("214-shipment-status.edi"), "--pretty").Output;

        Assert.DoesNotContain("\n  \"", compact.TrimEnd());
        Assert.Contains("\"delimiters\": {", pretty);
        Assert.True(pretty.Length > compact.Length);
    }

    [Fact]
    public void ParseReportsDiagnosticsInsideTheJsonRatherThanFailing()
    {
        CliResult result = Run("parse", Sample("214-broken.edi"));

        // parse describes the file; validate is the command that judges it.
        Assert.Equal(CliRunner.ExitOk, result.ExitCode);

        string[] codes = JsonDocument.Parse(result.Output).RootElement
            .GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("code").GetString()!)
            .ToArray();

        // Same order as validate: by position in the file, not by envelope nesting.
        Assert.Equal(new[] { "X12-SE01-COUNT", "X12-GE01-COUNT", "X12-IEA02-CONTROL" }, codes);
    }

    [Fact]
    public void ParseReadsTheInterchangeFromStdin()
    {
        CliResult result = Run(
            new[] { "parse" },
            stdin: File.ReadAllText(Sample("214-pipe-delimited.edi")));

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Equal(
            "|",
            JsonDocument.Parse(result.Output).RootElement
                .GetProperty("delimiters").GetProperty("element").GetString());
    }

    [Fact]
    public void ADashMeansStdinToo()
    {
        CliResult result = Run(
            new[] { "parse", "-" },
            stdin: File.ReadAllText(Sample("214-shipment-status.edi")));

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("DEMOSENDER", result.Output);
    }

    // -- validate ---------------------------------------------------------------------

    [Fact]
    public void ValidateExitsZeroOnASoundEnvelope()
    {
        CliResult result = Run("validate", Sample("214-shipment-status.edi"));

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("no diagnostics", result.Output);
    }

    [Fact]
    public void ValidateExitsOneWhenTheEnvelopeIsWrong()
    {
        CliResult result = Run("validate", Sample("214-broken.edi"));

        // This is the whole reason the tool is usable as a CI step.
        Assert.Equal(CliRunner.ExitDiagnostics, result.ExitCode);
    }

    [Fact]
    public void ValidateNamesEveryCodeAndOrdersThemByPositionInTheFile()
    {
        CliResult result = Run("validate", Sample("214-broken.edi"));

        Assert.Contains("X12-SE01-COUNT", result.Output);
        Assert.Contains("X12-GE01-COUNT", result.Output);
        Assert.Contains("X12-IEA02-CONTROL", result.Output);
        Assert.Contains("3 diagnostics", result.Output);

        // Validate() reports the envelope from the outside in; the CLI puts the
        // diagnostics back into the order they occur in the file.
        Assert.True(
            result.Output.IndexOf("X12-SE01-COUNT", StringComparison.Ordinal) <
            result.Output.IndexOf("X12-GE01-COUNT", StringComparison.Ordinal));
        Assert.True(
            result.Output.IndexOf("X12-GE01-COUNT", StringComparison.Ordinal) <
            result.Output.IndexOf("X12-IEA02-CONTROL", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateKeepsTheMessageDetailThatMakesItWorthReading()
    {
        CliResult result = Run("validate", Sample("214-broken.edi"));

        // The specificity is the product. A wrapped message must still contain the numbers.
        string flattened = string.Join(" ", result.Output.Split('\n', '\r')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0));

        Assert.Contains("SE01 (Number of Included Segments) declares '9'", flattened);
        Assert.Contains("contains 7 segments counting ST and SE", flattened);
    }

    [Fact]
    public void ValidateJsonCarriesTheCodesAndTheVerdict()
    {
        CliResult result = Run("validate", Sample("214-broken.edi"), "--json");

        Assert.Equal(CliRunner.ExitDiagnostics, result.ExitCode);

        JsonElement root = JsonDocument.Parse(result.Output).RootElement;
        Assert.False(root.GetProperty("valid").GetBoolean());
        Assert.Equal(3, root.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(9, root.GetProperty("diagnostics")[0].GetProperty("segmentPosition").GetInt32());
    }

    [Fact]
    public void ValidateJsonSaysValidWhenTheEnvelopeIsSound()
    {
        CliResult result = Run("validate", Sample("214-pipe-delimited.edi"), "--json");

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.True(JsonDocument.Parse(result.Output).RootElement.GetProperty("valid").GetBoolean());
    }

    // -- delimiters --------------------------------------------------------------------

    [Fact]
    public void DelimitersShowsTheFourCharactersOfAConventionalFile()
    {
        CliResult result = Run("delimiters", Sample("214-shipment-status.edi"));

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("element separator", result.Output);
        Assert.Contains("component separator", result.Output);
        Assert.Contains("segment terminator", result.Output);
        Assert.Contains("repetition separator", result.Output);
        Assert.Contains("ISA12 00501", result.Output);
    }

    [Fact]
    public void DelimitersRendersAControlCharacterTerminatorReadably()
    {
        CliResult result = Run("delimiters", Sample("214-pipe-delimited.edi"));

        // The pipe-delimited sample terminates its segments with a newline. Printing the
        // character itself would produce a blank cell.
        Assert.Contains("\\n", result.Output);
    }

    [Fact]
    public void DelimitersJsonCarriesTheRealCharacters()
    {
        CliResult result = Run("delimiters", Sample("214-pipe-delimited.edi"), "--json");

        JsonElement root = JsonDocument.Parse(result.Output).RootElement;
        Assert.Equal("|", root.GetProperty("element").GetString());
        Assert.Equal(">", root.GetProperty("component").GetString());
        Assert.Equal("\n", root.GetProperty("segment").GetString());
        Assert.Equal("!", root.GetProperty("repetition").GetString());
    }

    [Fact]
    public void DelimitersReportsNoRepetitionSeparatorForA4010Interchange()
    {
        // ISA11 is 'U' here, the Interchange Control Standards Identifier, not a delimiter.
        CliResult result = Run(new[] { "delimiters" }, stdin: Fixtures.Isa4010);

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("ISA12 00401", result.Output);
        Assert.Contains("where ISA11 is the standards identifier", result.Output);
    }

    [Fact]
    public void DelimitersJsonOmitsTheRepetitionSeparatorWhenThereIsNone()
    {
        CliResult result = Run(new[] { "delimiters", "--json" }, stdin: Fixtures.Isa4010);

        JsonElement root = JsonDocument.Parse(result.Output).RootElement;
        Assert.False(root.TryGetProperty("repetition", out _));
    }

    // -- output, colour ----------------------------------------------------------------

    [Fact]
    public void OutputWritesToTheFileAndLeavesStdoutEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            CliResult result = Run("parse", Sample("214-shipment-status.edi"), "--output", path);

            Assert.Equal(CliRunner.ExitOk, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("DEMOSENDER", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OutputToAnUnwritablePathIsAUsageErrorNotACrash()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "nested", "out.json");

        CliResult result = Run("parse", Sample("214-shipment-status.edi"), "--output", path);

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("cannot write", result.Error);
    }

    [Fact]
    public void NoEscapeSequencesReachARedirectedStream()
    {
        CliResult broken = Run("validate", Sample("214-broken.edi"));
        CliResult delimiters = Run("delimiters", Sample("214-shipment-status.edi"));

        Assert.DoesNotContain((char)0x1b, broken.Output);
        Assert.DoesNotContain((char)0x1b, delimiters.Output);
    }

    [Fact]
    public void ColourIsEmittedWhenTheTerminalTakesItAndWhenItIsForced()
    {
        CliResult terminal = Run(new[] { "validate", Sample("214-broken.edi") }, colorCapable: true);
        CliResult forced = Run(new[] { "validate", Sample("214-broken.edi"), "--color" }, stdin: string.Empty);

        Assert.Contains((char)0x1b, terminal.Output);
        Assert.Contains((char)0x1b, forced.Output);
    }

    [Fact]
    public void NoColourOverridesACapableTerminal()
    {
        CliResult result = Run(
            new[] { "validate", Sample("214-broken.edi"), "--no-color" },
            colorCapable: true);

        Assert.DoesNotContain((char)0x1b, result.Output);
    }

    [Fact]
    public void ColouredOutputIsThePlainOutputPlusEscapes()
    {
        string plain = Run("validate", Sample("214-broken.edi")).Output;
        string coloured = Run(new[] { "validate", Sample("214-broken.edi"), "--color" }, stdin: string.Empty).Output;

        Assert.Equal(plain, StripAnsi(coloured));
    }

    // -- usage, failures ---------------------------------------------------------------

    [Fact]
    public void HelpListsEveryCommandAndExitsZero()
    {
        CliResult result = Run("--help");

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("parse", result.Output);
        Assert.Contains("validate", result.Output);
        Assert.Contains("delimiters", result.Output);
        Assert.Contains("exit codes", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void VersionReportsTheToolAndTheLibraryItIsBuiltOn()
    {
        CliResult result = Run("--version");

        Assert.Equal(CliRunner.ExitOk, result.ExitCode);
        Assert.Contains("edix12 ", result.Output);
        Assert.Contains("EdiX12.Core ", result.Output);
        Assert.DoesNotContain("+", result.Output);
    }

    [Fact]
    public void NoArgumentsPrintsUsageOnStderrAndExitsThree()
    {
        CliResult result = Run(Array.Empty<string>(), stdin: string.Empty);

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("usage:", result.Error);
    }

    [Fact]
    public void AnUnknownCommandIsNamedBack()
    {
        CliResult result = Run("praise", Sample("214-shipment-status.edi"));

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("unknown command 'praise'", result.Error);
        Assert.Contains("--help", result.Error);
    }

    [Fact]
    public void AnUnknownOptionIsNamedBack()
    {
        CliResult result = Run("parse", Sample("214-shipment-status.edi"), "--prettyy");

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("unknown option '--prettyy'", result.Error);
    }

    [Fact]
    public void OutputWithNoPathIsAUsageError()
    {
        CliResult result = Run("parse", Sample("214-shipment-status.edi"), "--output");

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("needs a file path", result.Error);
    }

    [Fact]
    public void ASecondFileArgumentIsRejectedRatherThanIgnored()
    {
        CliResult result = Run("parse", Sample("214-shipment-status.edi"), Sample("214-broken.edi"));

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("One file at a time", result.Error);
    }

    [Fact]
    public void AMissingFileIsReportedByName()
    {
        CliResult result = Run("parse", Sample("no-such-file.edi"));

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("no such file", result.Error);
    }

    [Fact]
    public void NoFileAndNoPipeIsAUsageErrorRatherThanABlockingRead()
    {
        CliResult result = Run(new[] { "parse" }, stdinRedirected: false);

        Assert.Equal(CliRunner.ExitUsage, result.ExitCode);
        Assert.Contains("stdin is not a pipe", result.Error);
    }

    [Fact]
    public void AnUnreadableInterchangeExitsTwoAndKeepsTheLibrarysExplanation()
    {
        CliResult result = Run(new[] { "parse" }, stdin: "GS*QM*A*B*20260815*1430*1*X*005010~");

        Assert.Equal(CliRunner.ExitParseFailure, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("cannot parse", result.Error);
        Assert.Contains("not 'ISA'", result.Error);
    }

    [Fact]
    public void AStructuralFailureCarriesItsSegmentPosition()
    {
        // An SE with no ST is not something Validate() can report, because there is no
        // transaction set to attach the diagnostic to. It throws, and the position matters.
        string interchange = Fixtures.IsaStandard +
            "GS*QM*DEMOAPPSEND*DEMOAPPRECV*20260815*1430*1*X*005010~SE*2*0001~GE*1*1~IEA*1*000000001~";

        CliResult result = Run(new[] { "validate" }, stdin: interchange);

        Assert.Equal(CliRunner.ExitParseFailure, result.ExitCode);
        Assert.Contains("at segment 3", result.Error);
    }

    [Fact]
    public void EveryCommandRefusesAnUnreadableInterchangeTheSameWay()
    {
        foreach (string command in new[] { "parse", "validate", "delimiters" })
        {
            CliResult result = Run(new[] { command }, stdin: "not an interchange at all");

            Assert.Equal(CliRunner.ExitParseFailure, result.ExitCode);
            Assert.Contains("cannot parse <stdin>", result.Error);
        }
    }

    // -- harness -----------------------------------------------------------------------

    private sealed record CliResult(int ExitCode, string Output, string Error);

    private static CliResult Run(params string[] args) => Run(args, stdin: string.Empty);

    private static CliResult Run(
        string[] args,
        string stdin = "",
        bool stdinRedirected = true,
        bool colorCapable = false,
        int? terminalWidth = null)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };

        var environment = new CliEnvironment
        {
            In = new StringReader(stdin),
            Out = output,
            Error = error,
            StdinRedirected = stdinRedirected,
            ColorCapable = colorCapable,
            TerminalWidth = terminalWidth,
        };

        int exitCode = CliRunner.Run(args, environment);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Removes SGR sequences, to compare coloured output against plain.</summary>
    private static string StripAnsi(string text)
    {
        var stripped = new System.Text.StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != (char)0x1b)
            {
                stripped.Append(text[i]);
                continue;
            }

            // ESC '[' parameters 'm'
            while (i < text.Length && text[i] != 'm')
            {
                i++;
            }
        }

        return stripped.ToString();
    }
}
