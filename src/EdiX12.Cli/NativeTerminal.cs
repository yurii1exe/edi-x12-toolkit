using System.Runtime.InteropServices;

namespace EdiX12.Cli;

/// <summary>
/// Turns on ANSI escape processing for the Windows console.
/// </summary>
/// <remarks>
/// Windows consoles do not interpret SGR sequences unless the mode is set explicitly, and
/// a console that has not been told will print the escapes as text. Every failure path here
/// is silent and ends in the same place: colour is left off, and the output is the plain
/// text version, which is the whole point of routing every escape through
/// <see cref="Ansi"/>.
/// </remarks>
internal static class NativeTerminal
{
    private const int StdOutputHandle = -11;

    private const uint EnableVirtualTerminalProcessing = 0x0004;

    private static readonly IntPtr InvalidHandle = new IntPtr(-1);

    /// <summary>
    /// Enables virtual terminal processing on stdout. Returns true when the console will
    /// render escapes; false on any platform or console where it will not.
    /// </summary>
    internal static bool TryEnableVirtualTerminal()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Everything else already does this.
            return true;
        }

        try
        {
            IntPtr handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == InvalidHandle)
            {
                return false;
            }

            if (!GetConsoleMode(handle, out uint mode))
            {
                return false;
            }

            if ((mode & EnableVirtualTerminalProcessing) != 0)
            {
                return true;
            }

            return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
