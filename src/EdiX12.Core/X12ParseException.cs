namespace EdiX12.Core;

/// <summary>
/// Thrown when an interchange cannot be read at all — malformed ISA, missing envelope
/// segments, unreadable delimiters.
/// </summary>
/// <remarks>
/// Reserved for structural failures. Problems that a human still wants to see the parsed
/// document for — a control number that does not echo, a segment count that disagrees with
/// SE01 — are reported as <see cref="X12Diagnostic"/> from <see cref="Interchange.Validate"/>
/// instead of thrown. A parser that refuses to show you a bad file is useless for the one
/// job you actually need it for: working out why the partner rejected it.
/// </remarks>
public class X12ParseException : Exception
{
    /// <summary>Creates a parse exception.</summary>
    /// <param name="message">A message naming the offending construct and its location.</param>
    public X12ParseException(string message) : base(message)
    {
    }

    /// <summary>Creates a parse exception.</summary>
    /// <param name="message">A message naming the offending construct and its location.</param>
    /// <param name="innerException">The underlying cause.</param>
    public X12ParseException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// 1-based segment position the failure relates to, or <see langword="null"/> when the
    /// failure happened before segments could be identified.
    /// </summary>
    public int? SegmentPosition { get; set; }
}
