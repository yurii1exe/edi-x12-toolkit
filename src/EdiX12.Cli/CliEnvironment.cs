namespace EdiX12.Cli;

/// <summary>
/// Everything the CLI touches outside itself: the three standard streams, whether stdin is
/// a pipe, and whether stdout can render colour.
/// </summary>
/// <remarks>
/// Passing these in rather than reaching for <see cref="Console"/> is what lets the whole
/// tool — argument handling, rendering and exit codes — be driven from tests without
/// spawning a process or capturing a real console.
/// </remarks>
internal sealed class CliEnvironment
{
    /// <summary>Standard input, read only when the interchange comes from a pipe.</summary>
    public required TextReader In { get; init; }

    /// <summary>Standard output. Carries the result, and nothing else.</summary>
    public required TextWriter Out { get; init; }

    /// <summary>Standard error. Carries usage, parse failures and nothing that a pipe wants.</summary>
    public required TextWriter Error { get; init; }

    /// <summary>
    /// True when stdin is a pipe or a file. Checked before reading, so that
    /// <c>edix12 parse</c> with no arguments prints usage instead of hanging on a
    /// terminal that is never going to send anything.
    /// </summary>
    public bool StdinRedirected { get; init; }

    /// <summary>
    /// True when stdout is a terminal that can render ANSI escapes. Only the default —
    /// <c>--color</c> and <c>--no-color</c> override it.
    /// </summary>
    public bool ColorCapable { get; init; }

    /// <summary>
    /// Width of the terminal, or <see langword="null"/> when there isn't one. Null gives a
    /// fixed width, so that redirected output is byte-identical whatever window the command
    /// happened to be run in — which is what makes it safe to paste into a README.
    /// </summary>
    public int? TerminalWidth { get; init; }
}
