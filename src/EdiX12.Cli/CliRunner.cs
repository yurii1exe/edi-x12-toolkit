using System.Reflection;
using EdiX12.Core;

namespace EdiX12.Cli;

/// <summary>
/// The whole tool, driven from arguments and three streams.
/// </summary>
/// <remarks>
/// <para>
/// Three commands, one input, one output. <c>parse</c> writes JSON because that is the
/// only thing a pipeline can do anything with; <c>validate</c> and <c>delimiters</c> write
/// for a human by default and JSON on request, because those two are usually read rather
/// than piped.
/// </para>
/// <para>
/// Exit codes are the reason the tool is useful in CI: <c>validate</c> returns 1 when the
/// envelope has anything wrong with it, so a build step can simply run it.
/// </para>
/// </remarks>
internal static class CliRunner
{
    /// <summary>The command did what it was asked.</summary>
    internal const int ExitOk = 0;

    /// <summary><c>validate</c> found at least one diagnostic. The file parsed.</summary>
    internal const int ExitDiagnostics = 1;

    /// <summary>The interchange could not be read at all.</summary>
    internal const int ExitParseFailure = 2;

    /// <summary>Bad arguments, missing file, unwritable output.</summary>
    internal const int ExitUsage = 3;

    /// <summary>
    /// Line width used when stdout is not a terminal. Fixed on purpose: redirected output
    /// must not change shape depending on the window it was produced in.
    /// </summary>
    private const int RedirectedWidth = 100;

    private const int MinimumWidth = 60;

    private const int MaximumWidth = 120;

    /// <summary>The name the tool is invoked by, used to prefix messages on stderr.</summary>
    private const string ToolName = "edix12";

    /// <summary>Runs one invocation.</summary>
    /// <param name="args">The raw arguments, exactly as the shell handed them over.</param>
    /// <param name="env">Streams, redirection state and terminal capability.</param>
    /// <returns>The process exit code.</returns>
    internal static int Run(string[] args, CliEnvironment env)
    {
        if (!Options.TryParse(args, out Options options, out string? argumentError))
        {
            return Fail(env, Palette(options, env), argumentError!, suggestHelp: true);
        }

        Ansi ansi = Palette(options, env);

        if (options.Help)
        {
            Write(env.Out, Usage());
            return ExitOk;
        }

        if (options.Version)
        {
            Write(env.Out, Versions());
            return ExitOk;
        }

        if (options.Command is null)
        {
            Write(env.Error, Usage());
            return ExitUsage;
        }

        if (options.Command is not ("parse" or "validate" or "delimiters"))
        {
            return Fail(env, ansi, $"unknown command '{options.Command}'.", suggestHelp: true);
        }

        if (!TryReadInput(options, env, out string text, out string label, out string? inputError))
        {
            return Fail(env, ansi, inputError!, suggestHelp: options.Path is null);
        }

        try
        {
            return options.Command switch
            {
                "parse" => Parse(text, options, env, ansi),
                "validate" => Validate(text, label, options, env, ansi),
                _ => Delimiters(text, label, options, env, ansi),
            };
        }
        catch (X12ParseException exception)
        {
            return ReportParseFailure(env, ansi, label, exception);
        }
    }

    /// <summary>Parses an interchange and writes it, with any diagnostics, as one JSON document.</summary>
    /// <remarks>
    /// Diagnostics ride along in the JSON rather than going to stderr: a caller that has
    /// asked for a machine-readable document should not have to reassemble it from two
    /// streams. Finding diagnostics is not a failure here — <c>validate</c> is the command
    /// that reports them through the exit code.
    /// </remarks>
    private static int Parse(string text, Options options, CliEnvironment env, Ansi ansi)
    {
        Interchange interchange = X12Parser.Parse(text);
        string json = JsonOutput.Serialize(
            JsonOutput.Describe(interchange, InFileOrder(interchange)),
            options.Pretty);

        return Emit(json + "\n", options, env, ansi, ExitOk);
    }

    /// <summary>Runs the envelope checks and reports them, exiting non-zero if there are any.</summary>
    private static int Validate(string text, string label, Options options, CliEnvironment env, Ansi ansi)
    {
        Interchange interchange = X12Parser.Parse(text);

        X12Diagnostic[] diagnostics = InFileOrder(interchange);

        string content = options.Json
            ? JsonOutput.Serialize(
                new ValidationResultDto(
                    label,
                    diagnostics.Length == 0,
                    Array.ConvertAll(diagnostics, JsonOutput.Describe)),
                options.Pretty) + "\n"
            : TextOutput.Diagnostics(label, diagnostics, Width(env), ansi);

        return Emit(content, options, env, ansi, diagnostics.Length == 0 ? ExitOk : ExitDiagnostics);
    }

    /// <summary>
    /// The envelope diagnostics, in the order they occur in the file.
    /// </summary>
    /// <remarks>
    /// <see cref="Interchange.Validate"/> walks the envelope from the outside in, so it
    /// reports IEA before GE before SE — the reverse of the order they are written in.
    /// Sorting by segment position puts them back, which is what someone reading the file
    /// alongside the output wants, and it is the same order in every command.
    /// </remarks>
    private static X12Diagnostic[] InFileOrder(Interchange interchange) =>
        interchange.Validate().OrderBy(d => d.SegmentPosition).ToArray();

    /// <summary>Reads the delimiters out of the ISA and shows where each one came from.</summary>
    /// <remarks>
    /// Tokenizing rather than calling <see cref="X12Tokenizer.ReadDelimiters"/> costs
    /// nothing and yields the ISA segment as well, which is needed for ISA12 — the version
    /// that decides whether ISA11 is a delimiter at all.
    /// </remarks>
    private static int Delimiters(string text, string label, Options options, CliEnvironment env, Ansi ansi)
    {
        IReadOnlyList<Segment> segments = X12Tokenizer.Tokenize(text, out X12Delimiters delimiters);
        string isaVersion = segments[0][12].Trim();

        string content = options.Json
            ? JsonOutput.Serialize(JsonOutput.Describe(delimiters), options.Pretty) + "\n"
            : TextOutput.Delimiters(label, isaVersion, delimiters, ansi);

        return Emit(content, options, env, ansi, ExitOk);
    }

    /// <summary>Writes the result to stdout, or to the file <c>--output</c> named.</summary>
    private static int Emit(string content, Options options, CliEnvironment env, Ansi ansi, int successExit)
    {
        if (options.Output is null)
        {
            Write(env.Out, content);
            return successExit;
        }

        try
        {
            File.WriteAllText(options.Output, WithLineEndings(content, Environment.NewLine));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Fail(env, ansi, $"cannot write '{options.Output}': {exception.Message}", suggestHelp: false);
        }

        return successExit;
    }

    /// <summary>
    /// Resolves the input: the named file, or stdin when the argument is <c>-</c> or absent.
    /// </summary>
    /// <remarks>
    /// The redirection check matters more than it looks. Without it, <c>edix12 parse</c>
    /// typed at a prompt would block on a terminal that is never going to send anything,
    /// and read as a hung tool rather than a missing argument.
    /// </remarks>
    private static bool TryReadInput(
        Options options,
        CliEnvironment env,
        out string text,
        out string label,
        out string? error)
    {
        text = string.Empty;
        label = "<stdin>";
        error = null;

        if (options.Path is null)
        {
            if (!env.StdinRedirected)
            {
                error = "no input file given, and stdin is not a pipe. Pass a file, or pipe one in.";
                return false;
            }

            text = env.In.ReadToEnd();
            return true;
        }

        label = options.Path;

        try
        {
            text = File.ReadAllText(options.Path);
            return true;
        }
        catch (FileNotFoundException)
        {
            error = $"no such file: {options.Path}";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            error = $"no such file: {options.Path}";
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"cannot read '{options.Path}': {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Reports a structural failure. These are the ones worth being verbose about: the
    /// library's exception messages name the offending construct and its offset, and
    /// throwing that away in favour of "parse error" would waste the work.
    /// </summary>
    private static int ReportParseFailure(CliEnvironment env, Ansi ansi, string label, X12ParseException exception)
    {
        string position = exception.SegmentPosition.HasValue
            ? $" at segment {exception.SegmentPosition.Value}"
            : string.Empty;

        string message =
            ansi.Paint(ansi.Bold + ansi.Red, ToolName + ":") + $" cannot parse {label}{position}\n" +
            string.Join("\n", TextOutput.Wrap(exception.Message, Width(env) - 2).Select(line => "  " + line)) +
            "\n";

        Write(env.Error, message);
        return ExitParseFailure;
    }

    private static int Fail(CliEnvironment env, Ansi ansi, string message, bool suggestHelp)
    {
        Write(env.Error, ansi.Paint(ansi.Bold + ansi.Red, ToolName + ":") + " " + message + "\n");

        if (suggestHelp)
        {
            Write(env.Error, $"Try '{ToolName} --help'.\n");
        }

        return ExitUsage;
    }

    /// <summary>
    /// Colour is on only when the terminal can take it and the result is not being written
    /// to a file, unless the caller has said otherwise with <c>--color</c>.
    /// </summary>
    private static Ansi Palette(Options options, CliEnvironment env) =>
        new Ansi(options.Color ?? (env.ColorCapable && options.Output is null));

    private static int Width(CliEnvironment env) =>
        Math.Clamp(env.TerminalWidth ?? RedirectedWidth, MinimumWidth, MaximumWidth);

    /// <summary>Writes with the stream's own line endings, so redirected output is native.</summary>
    private static void Write(TextWriter writer, string content) =>
        writer.Write(WithLineEndings(content, writer.NewLine));

    /// <summary>
    /// Rewrites every line ending to <paramref name="newLine"/>.
    /// </summary>
    /// <remarks>
    /// The collapse to LF first is not redundant. Indented output from
    /// <c>System.Text.Json</c> already breaks lines with <see cref="Environment.NewLine"/>,
    /// so on Windows a naive replace of <c>\n</c> turns every <c>\r\n</c> into
    /// <c>\r\r\n</c> — invisible in a terminal, and wrong in a file.
    /// </remarks>
    private static string WithLineEndings(string content, string newLine) =>
        content.Replace("\r\n", "\n").Replace("\n", newLine);

    private static string Usage() =>
        "edix12 - read ANSI X12 freight EDI from the command line\n" +
        "\n" +
        "usage:\n" +
        "  edix12 <command> [file] [options]\n" +
        "\n" +
        "commands:\n" +
        "  parse         parse an interchange and write it, with its diagnostics, as JSON\n" +
        "  validate      check the envelope; exits 1 if anything is wrong with it\n" +
        "  delimiters    show the four delimiters the ISA declares, and where each was read\n" +
        "\n" +
        "options:\n" +
        "  --pretty            indent the JSON\n" +
        "  --json              JSON from validate and delimiters instead of the table\n" +
        "  -o, --output PATH   write to a file instead of stdout\n" +
        "  --color, --no-color force colour on or off (default: on when stdout is a terminal)\n" +
        "  -h, --help          this text\n" +
        "  --version           version of the tool and of the library it is built on\n" +
        "\n" +
        "The file may be '-', or left out entirely, to read the interchange from stdin.\n" +
        "\n" +
        "exit codes:\n" +
        "  0   the command succeeded\n" +
        "  1   validate found diagnostics\n" +
        "  2   the interchange could not be parsed at all\n" +
        "  3   bad arguments, or the file could not be read or written\n";

    private static string Versions() =>
        $"{ToolName} {VersionOf(typeof(CliRunner).Assembly)}\n" +
        $"EdiX12.Core {VersionOf(typeof(X12Parser).Assembly)}\n";

    /// <summary>
    /// The package version. Anything after a <c>+</c> is build metadata, not something a
    /// reader of <c>--version</c> asked for.
    /// </summary>
    private static string VersionOf(Assembly assembly)
    {
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

        int metadata = version.IndexOf('+');
        return metadata < 0 ? version : version.Substring(0, metadata);
    }
}
