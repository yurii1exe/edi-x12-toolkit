using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EdiX12.Core;

namespace EdiX12.Cli;

/// <summary>
/// Projects the parsed object graph onto a stable JSON shape.
/// </summary>
/// <remarks>
/// <para>
/// The domain types are deliberately not serialised directly. A JSON document that a script
/// pipes into <c>jq</c> is a public contract, and tying it to the reflected shape of the
/// library's classes would mean that renaming a property is a breaking change to the CLI.
/// The DTOs below are that contract, written out once.
/// </para>
/// <para>
/// Delimiters are emitted as the real characters, not as their printable descriptions, so
/// that a segment terminator of newline arrives as <c>"\n"</c> and round-trips. The
/// human-readable rendering is <see cref="TextOutput"/>'s job.
/// </para>
/// </remarks>
internal static class JsonOutput
{
    private static readonly JsonSerializerOptions Compact = Configure(indented: false);
    private static readonly JsonSerializerOptions Indented = Configure(indented: true);

    /// <summary>Serialises <paramref name="value"/>, indented when <paramref name="pretty"/>.</summary>
    internal static string Serialize<T>(T value, bool pretty) =>
        JsonSerializer.Serialize(value, pretty ? Indented : Compact);

    private static JsonSerializerOptions Configure(bool indented) => new JsonSerializerOptions
    {
        WriteIndented = indented,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // The default encoder escapes '>' and '&' as > and & for HTML safety.
        // This output goes to a terminal and to jq, and '>' is a perfectly ordinary X12
        // component separator, so escaping it makes the one field that matters unreadable.
        // Control characters are still escaped by the writer regardless of this setting.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The four delimiters, as the characters themselves.</summary>
    internal static DelimitersDto Describe(X12Delimiters delimiters) => new DelimitersDto(
        delimiters.Element.ToString(),
        delimiters.Component.ToString(),
        delimiters.Segment.ToString(),
        delimiters.Repetition?.ToString());

    /// <summary>The whole interchange: delimiters, envelope tree, and any diagnostics.</summary>
    /// <param name="interchange">The parsed interchange.</param>
    /// <param name="diagnostics">
    /// Its diagnostics, already ordered by the caller so that every command emits them the
    /// same way round.
    /// </param>
    internal static ParseResultDto Describe(
        Interchange interchange,
        IReadOnlyList<X12Diagnostic> diagnostics) => new ParseResultDto(
        Describe(interchange.Delimiters),
        new InterchangeDto(
            interchange.SenderQualifier,
            interchange.SenderId,
            interchange.ReceiverQualifier,
            interchange.ReceiverId,

            // Round-trip format without a zone designator. ISA carries no time zone, and
            // stamping a 'Z' on it would be an assertion the standard does not make.
            interchange.InterchangeDate?.ToString("s"),
            interchange.VersionNumber,
            interchange.ControlNumber,
            interchange.AcknowledgmentRequested,
            interchange.UsageIndicator,
            interchange.IsProduction,
            interchange.Segments.Count,
            interchange.Groups.Select(Describe).ToArray()),
        diagnostics.Select(Describe).ToArray());

    /// <summary>One diagnostic.</summary>
    internal static DiagnosticDto Describe(X12Diagnostic diagnostic) =>
        new DiagnosticDto(diagnostic.Code, diagnostic.Message, diagnostic.SegmentPosition);

    private static GroupDto Describe(FunctionalGroup group) => new GroupDto(
        group.FunctionalIdentifierCode,
        group.ApplicationSenderCode,
        group.ApplicationReceiverCode,
        group.ControlNumber,
        group.VersionReleaseIndustryCode,
        group.Transactions.Select(Describe).ToArray());

    private static TransactionDto Describe(TransactionSet transaction) => new TransactionDto(
        transaction.IdentifierCode,
        transaction.ControlNumber,
        NullIfEmpty(transaction.ImplementationConventionReference),
        transaction.DeclaredSegmentCount,
        transaction.Segments.Select(Describe).ToArray());

    private static SegmentDto Describe(Segment segment) =>
        new SegmentDto(segment.Id, segment.Position, segment.Elements);

    /// <summary>
    /// Absent and empty are the same thing in X12, and an omitted key says that more
    /// honestly than <c>""</c> does.
    /// </summary>
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}

/// <summary>The delimiters an interchange declared, as the characters themselves.</summary>
internal sealed record DelimitersDto(string Element, string Component, string Segment, string? Repetition);

/// <summary>One segment: its identifier, its position in the interchange, its elements.</summary>
internal sealed record SegmentDto(string Id, int Position, IReadOnlyList<string> Elements);

/// <summary>One transaction set and its body segments, exclusive of ST and SE.</summary>
internal sealed record TransactionDto(
    string IdentifierCode,
    string ControlNumber,
    string? ImplementationConventionReference,
    int DeclaredSegmentCount,
    IReadOnlyList<SegmentDto> Segments);

/// <summary>One functional group and its transaction sets.</summary>
internal sealed record GroupDto(
    string FunctionalIdentifierCode,
    string ApplicationSenderCode,
    string ApplicationReceiverCode,
    string ControlNumber,
    string VersionReleaseIndustryCode,
    IReadOnlyList<TransactionDto> Transactions);

/// <summary>The interchange envelope and its groups.</summary>
internal sealed record InterchangeDto(
    string SenderQualifier,
    string SenderId,
    string ReceiverQualifier,
    string ReceiverId,
    string? Date,
    string VersionNumber,
    string ControlNumber,
    bool AcknowledgmentRequested,
    string UsageIndicator,
    bool IsProduction,
    int SegmentCount,
    IReadOnlyList<GroupDto> Groups);

/// <summary>One envelope problem.</summary>
internal sealed record DiagnosticDto(string Code, string Message, int SegmentPosition);

/// <summary>What <c>edix12 parse</c> writes.</summary>
internal sealed record ParseResultDto(
    DelimitersDto Delimiters,
    InterchangeDto Interchange,
    IReadOnlyList<DiagnosticDto> Diagnostics);

/// <summary>What <c>edix12 validate --json</c> writes.</summary>
internal sealed record ValidationResultDto(
    string File,
    bool Valid,
    IReadOnlyList<DiagnosticDto> Diagnostics);
