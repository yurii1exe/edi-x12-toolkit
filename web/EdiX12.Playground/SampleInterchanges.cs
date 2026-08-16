using System.Reflection;

namespace EdiX12.Playground;

/// <summary>
/// One of the repository's own sample interchanges, offered as a one-click example.
/// </summary>
/// <param name="FileName">Path within the repository, so a visitor can find the file.</param>
/// <param name="Label">Short button label.</param>
/// <param name="Blurb">What this sample is here to demonstrate.</param>
/// <param name="Text">The interchange itself, byte-for-byte as committed.</param>
public sealed record SampleInterchange(string FileName, string Label, string Blurb, string Text);

/// <summary>
/// The samples, embedded at build time from <c>samples/</c> in the repository root.
/// </summary>
/// <remarks>
/// Embedded rather than fetched: the page promises it makes no network calls once it has
/// loaded, and a <c>fetch</c> for a sample file would make that untrue.
/// </remarks>
public static class SampleInterchanges
{
    private static readonly Lazy<IReadOnlyList<SampleInterchange>> Lazy = new(Load);

    /// <summary>The samples, in the order they should be offered.</summary>
    public static IReadOnlyList<SampleInterchange> All => Lazy.Value;

    private static IReadOnlyList<SampleInterchange> Load()
    {
        return
        [
            Read("214-broken.edi", "Broken envelope",
                "Three counts and echoes disagree with reality. Shows what a partner rejection actually looks like."),
            Read("214-shipment-status.edi", "214 shipment status",
                "A well-formed 5010 214, delimited the conventional way with * and ~."),
            Read("214-pipe-delimited.edi", "Same file, pipe-delimited",
                "Byte-for-byte the same document delimited with | and terminated by a newline. Parses identically."),
            Read("214-4010.edi", "4010 interchange",
                "ISA11 is 'U' here — the standards identifier, not a repetition separator. Reading it as a delimiter splits values on the letter U."),
        ];
    }

    private static SampleInterchange Read(string fileName, string label, string blurb)
    {
        Assembly assembly = typeof(SampleInterchanges).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("samples/" + fileName);

        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Sample '{fileName}' was not embedded. Check the EmbeddedResource items in EdiX12.Playground.csproj.");
        }

        // No BOM detection and no newline translation: the pipe-delimited sample uses a
        // newline as its segment terminator, so rewriting line endings would change what
        // the file means.
        using var reader = new StreamReader(stream);
        return new SampleInterchange(fileName, label, blurb, reader.ReadToEnd());
    }
}
