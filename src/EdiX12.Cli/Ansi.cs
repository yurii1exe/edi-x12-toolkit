namespace EdiX12.Cli;

/// <summary>
/// SGR escape sequences, or empty strings when colour is switched off.
/// </summary>
/// <remarks>
/// Every escape goes through here so that "no colour" is a single decision made once, in
/// <c>Program</c>, rather than a condition repeated at every write. Output that is piped to
/// a file or another process is byte-identical to the coloured version minus the escapes —
/// anything else makes the tool unusable in a script.
/// </remarks>
internal sealed class Ansi
{
    /// <summary>
    /// Control Sequence Introducer: ESC (0x1B) followed by <c>[</c>. Built from the code
    /// point rather than written as a literal, because a raw ESC byte in a source file does
    /// not survive editors, diff tools or copy-paste.
    /// </summary>
    private static readonly string Csi = (char)0x1b + "[";

    private readonly bool _enabled;

    internal Ansi(bool enabled) => _enabled = enabled;

    /// <summary>A palette that emits nothing, for redirected output and for tests.</summary>
    internal static Ansi None { get; } = new Ansi(false);

    internal string Reset => Code("0m");

    internal string Bold => Code("1m");

    /// <summary>Faint. Used for explanatory notes, which are secondary to the values.</summary>
    internal string Dim => Code("2m");

    internal string Red => Code("31m");

    internal string Green => Code("32m");

    internal string Yellow => Code("33m");

    internal string Cyan => Code("36m");

    /// <summary>Wraps <paramref name="text"/> in <paramref name="style"/> and a reset.</summary>
    internal string Paint(string style, string text) =>
        style.Length > 0 ? style + text + Reset : text;

    private string Code(string parameters) => _enabled ? Csi + parameters : string.Empty;
}
