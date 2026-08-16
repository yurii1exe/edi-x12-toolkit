namespace EdiX12.Cli;

/// <summary>The command line, parsed.</summary>
internal sealed class Options
{
    /// <summary>The sub-command, or <see langword="null"/> when none was given.</summary>
    public string? Command { get; private set; }

    /// <summary>
    /// The input file, or <see langword="null"/> to read stdin. <c>-</c> is normalised to
    /// <see langword="null"/> so the two ways of asking for stdin take the same path.
    /// </summary>
    public string? Path { get; private set; }

    /// <summary>Indent JSON output.</summary>
    public bool Pretty { get; private set; }

    /// <summary>Emit JSON from commands whose default is the human-readable rendering.</summary>
    public bool Json { get; private set; }

    /// <summary>Write the result to this file instead of stdout.</summary>
    public string? Output { get; private set; }

    /// <summary>Colour override: <see langword="null"/> means "decide from the terminal".</summary>
    public bool? Color { get; private set; }

    /// <summary>Print usage and stop.</summary>
    public bool Help { get; private set; }

    /// <summary>Print versions and stop.</summary>
    public bool Version { get; private set; }

    /// <summary>
    /// Whether a file argument was supplied at all. Distinct from <see cref="Path"/> being
    /// null, because <c>-</c> is a supplied argument that resolves to stdin.
    /// </summary>
    private bool PathSupplied { get; set; }

    /// <summary>
    /// Parses arguments. Returns <see langword="false"/> and a message naming the offending
    /// argument rather than throwing, because a usage error is an expected outcome for a
    /// CLI and deserves its own exit code, not a stack trace.
    /// </summary>
    public static bool TryParse(string[] args, out Options options, out string? error)
    {
        var parsed = new Options();
        options = parsed;
        error = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    parsed.Help = true;
                    continue;

                case "--version":
                    parsed.Version = true;
                    continue;

                case "--pretty":
                    parsed.Pretty = true;
                    continue;

                case "--json":
                    parsed.Json = true;
                    continue;

                case "--color":
                case "--colour":
                    parsed.Color = true;
                    continue;

                case "--no-color":
                case "--no-colour":
                    parsed.Color = false;
                    continue;

                case "-o":
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        error = $"'{arg}' needs a file path after it.";
                        return false;
                    }

                    parsed.Output = args[++i];
                    continue;
            }

            // '-' is the conventional name for stdin and must be tested before the
            // leading-dash check below, which is what rejects mistyped flags.
            if (arg.Length > 1 && arg[0] == '-')
            {
                error = $"unknown option '{arg}'.";
                return false;
            }

            if (parsed.Command is null)
            {
                parsed.Command = arg;
            }
            else if (!parsed.PathSupplied)
            {
                parsed.PathSupplied = true;
                parsed.Path = arg == "-" ? null : arg;
            }
            else
            {
                error = $"unexpected argument '{arg}'. One file at a time.";
                return false;
            }
        }

        return true;
    }
}
