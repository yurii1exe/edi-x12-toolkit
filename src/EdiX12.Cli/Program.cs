using EdiX12.Cli;

// Everything the tool does lives in CliRunner.Run, which takes its streams as arguments. This
// file is the only place that touches the real console, so that the rest is testable.
bool ansiCapable = NativeTerminal.TryEnableVirtualTerminal();

var environment = new CliEnvironment
{
    In = Console.In,
    Out = Console.Out,
    Error = Console.Error,
    StdinRedirected = Console.IsInputRedirected,
    ColorCapable = ansiCapable && !Console.IsOutputRedirected && !ColorSuppressedByEnvironment(),
    TerminalWidth = TerminalWidth(),
};

return CliRunner.Run(args, environment);

// https://no-color.org - any non-empty value means "do not emit colour", and TERM=dumb is
// the older convention that says the same thing.
static bool ColorSuppressedByEnvironment() =>
    Environment.GetEnvironmentVariable("NO_COLOR") is { Length: > 0 } ||
    string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

// Null when there is no console to measure, which is also when the width must be fixed so
// that redirected output does not vary with the window it was produced in.
static int? TerminalWidth()
{
    if (Console.IsOutputRedirected)
    {
        return null;
    }

    try
    {
        int width = Console.WindowWidth;
        return width > 0 ? width : null;
    }
    catch (IOException)
    {
        return null;
    }
}
