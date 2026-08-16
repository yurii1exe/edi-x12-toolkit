namespace EdiX12.Core;

/// <summary>
/// A single problem found in an otherwise readable interchange.
/// </summary>
/// <remarks>
/// The messages here are deliberately verbose. "Parse error" costs an hour of someone's
/// afternoon; "SE01 declares 12 segments, interchange contains 11, in transaction set
/// 0001 at segment 4" costs about ten seconds.
/// </remarks>
public sealed class X12Diagnostic
{
    /// <summary>Creates a diagnostic.</summary>
    /// <param name="code">Stable machine-readable code, e.g. <c>X12-SE01-COUNT</c>.</param>
    /// <param name="message">Human-readable description naming the element and location.</param>
    /// <param name="segmentPosition">1-based segment position within the interchange.</param>
    public X12Diagnostic(string code, string message, int segmentPosition)
    {
        Code = code;
        Message = message;
        SegmentPosition = segmentPosition;
    }

    /// <summary>Stable machine-readable code, safe to switch on.</summary>
    public string Code { get; }

    /// <summary>Human-readable description, safe to log or show to an operator.</summary>
    public string Message { get; }

    /// <summary>1-based segment position within the interchange.</summary>
    public int SegmentPosition { get; }

    /// <summary>Renders as <c>CODE at segment N: message</c>.</summary>
    public override string ToString() => $"{Code} at segment {SegmentPosition}: {Message}";
}
