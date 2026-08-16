using System.Globalization;

namespace EdiX12.Core;

/// <summary>
/// One X12 interchange: the ISA/IEA envelope and everything inside it.
/// </summary>
/// <remarks>
/// <para>The X12 envelope nests three levels deep, and each level has a control number
/// that its trailer must echo back:</para>
/// <code>
/// ISA  interchange header          control number ISA13
///   GS  functional group header    control number GS06
///     ST  transaction set header   control number ST02
///     ...  the actual business data
///     SE  transaction set trailer  SE01 = segment count, SE02 = ST02
///   GE  functional group trailer   GE01 = transaction count, GE02 = GS06
/// IEA  interchange trailer         IEA01 = group count, IEA02 = ISA13
/// </code>
/// <para>Those echoes and counts are what a receiver checks first. Get one wrong and the
/// partner rejects the whole interchange, usually with an error message that does not say
/// which one. <see cref="Validate"/> checks all nine: the three counts, the three control
/// number echoes, and the presence of the three trailer segments themselves.</para>
/// </remarks>
public sealed class Interchange
{
    /// <summary>
    /// Two-digit years in ISA09 are pivoted here. Anything at or above this is read as
    /// 19xx. X12 never fixed this: ISA09 is YYMMDD while GS04 became CCYYMMDD in 5010,
    /// so the interchange header is the less precise of the two.
    /// </summary>
    private const int TwoDigitYearPivot = 70;

    private readonly Segment _isa;
    private readonly Segment? _iea;

    internal Interchange(
        Segment isa,
        Segment? iea,
        IReadOnlyList<FunctionalGroup> groups,
        IReadOnlyList<Segment> segments,
        X12Delimiters delimiters)
    {
        _isa = isa;
        _iea = iea;
        Groups = groups;
        Segments = segments;
        Delimiters = delimiters;
    }

    /// <summary>The delimiters this interchange declared in its ISA.</summary>
    public X12Delimiters Delimiters { get; }

    /// <summary>Every segment in the interchange, in order, ISA first.</summary>
    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>The functional groups (GS/GE) in this interchange.</summary>
    public IReadOnlyList<FunctionalGroup> Groups { get; }

    /// <summary>The raw ISA segment.</summary>
    public Segment Header => _isa;

    /// <summary>The raw IEA segment, or <see langword="null"/> if the sender omitted it.</summary>
    public Segment? Trailer => _iea;

    /// <summary>ISA05, the qualifier that says what kind of identifier <see cref="SenderId"/> is (<c>ZZ</c>, <c>01</c> DUNS, <c>02</c> SCAC).</summary>
    public string SenderQualifier => _isa[5].Trim();

    /// <summary>ISA06, the interchange sender ID, space-padded to 15 on the wire and trimmed here.</summary>
    public string SenderId => _isa[6].Trim();

    /// <summary>ISA07, the qualifier for <see cref="ReceiverId"/>.</summary>
    public string ReceiverQualifier => _isa[7].Trim();

    /// <summary>ISA08, the interchange receiver ID.</summary>
    public string ReceiverId => _isa[8].Trim();

    /// <summary>ISA12, the interchange control version number, e.g. <c>00501</c> for 5010.</summary>
    public string VersionNumber => _isa[12].Trim();

    /// <summary>ISA13, the interchange control number. The IEA02 of the trailer must repeat it.</summary>
    public string ControlNumber => _isa[13].Trim();

    /// <summary>ISA14: <c>1</c> if the sender wants a TA1 interchange acknowledgment, <c>0</c> if not.</summary>
    public bool AcknowledgmentRequested => _isa[14].Trim() == "1";

    /// <summary>ISA15, the usage indicator: <c>P</c> production, <c>T</c> test.</summary>
    public string UsageIndicator => _isa[15].Trim();

    /// <summary>
    /// True when ISA15 is <c>P</c>. Worth checking explicitly before you act on a document —
    /// partners send test interchanges into production endpoints more often than anyone admits.
    /// </summary>
    public bool IsProduction => UsageIndicator == "P";

    /// <summary>
    /// ISA09 and ISA10 combined, or <see langword="null"/> if either is unparseable.
    /// </summary>
    /// <remarks>
    /// <para>The value has no time zone: X12 does not carry one, and by convention it is the
    /// sender's local time. Do not compare it to <c>DateTime.UtcNow</c> without agreeing
    /// a zone with the partner first.</para>
    /// <para>ISA09 is <c>YYMMDD</c>, so the century has to be inferred. Two-digit years of
    /// 70 and above are read as 19xx, below 70 as 20xx. X12 never fixed this at the
    /// interchange level — GS04 became <c>CCYYMMDD</c> in 5010, which makes
    /// <see cref="FunctionalGroup"/> the more precise source of a date if you have one.</para>
    /// </remarks>
    public DateTime? InterchangeDate
    {
        get
        {
            string date = _isa[9].Trim();
            string time = _isa[10].Trim();

            if (date.Length != 6 ||
                !int.TryParse(date.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int yy) ||
                !DateTime.TryParseExact(date.Substring(2, 4), "MMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime monthDay))
            {
                return null;
            }

            int year = yy >= TwoDigitYearPivot ? 1900 + yy : 2000 + yy;

            int hours = 0;
            int minutes = 0;
            if (time.Length >= 4)
            {
                int.TryParse(time.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out hours);
                int.TryParse(time.Substring(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out minutes);
            }

            if (hours > 23 || minutes > 59)
            {
                return null;
            }

            return new DateTime(year, monthDay.Month, monthDay.Day, hours, minutes, 0, DateTimeKind.Unspecified);
        }
    }

    /// <summary>Every transaction set in every group, flattened.</summary>
    public IEnumerable<TransactionSet> Transactions => Groups.SelectMany(g => g.Transactions);

    /// <summary>
    /// Checks the nine envelope rules a receiving partner checks before it looks at the
    /// business data: the three trailer echoes, the three counts, and that the three
    /// trailer segments are present at all.
    /// </summary>
    /// <returns>
    /// The problems found, outermost envelope first — interchange, then each group, then
    /// each transaction set — so the order is by nesting rather than by segment position.
    /// Each carries a stable code:
    /// <c>X12-IEA-MISSING</c>, <c>X12-IEA01-COUNT</c>, <c>X12-IEA02-CONTROL</c>,
    /// <c>X12-GE-MISSING</c>, <c>X12-GE01-COUNT</c>, <c>X12-GE02-CONTROL</c>,
    /// <c>X12-SE-MISSING</c>, <c>X12-SE01-COUNT</c>, <c>X12-SE02-CONTROL</c>.
    /// An empty list means the envelope is structurally sound — it says nothing about
    /// whether the business data is valid.
    /// </returns>
    public IReadOnlyList<X12Diagnostic> Validate()
    {
        var diagnostics = new List<X12Diagnostic>();

        if (_iea is null)
        {
            diagnostics.Add(new X12Diagnostic(
                "X12-IEA-MISSING",
                "Interchange has no IEA trailer segment. Every ISA must be closed by an IEA.",
                Segments.Count));
        }
        else
        {
            string declaredGroups = _iea[1].Trim();
            if (declaredGroups != Groups.Count.ToString(CultureInfo.InvariantCulture))
            {
                diagnostics.Add(new X12Diagnostic(
                    "X12-IEA01-COUNT",
                    $"IEA01 (Number of Included Functional Groups) declares '{declaredGroups}', " +
                    $"interchange contains {Groups.Count}.",
                    _iea.Position));
            }

            string echoed = _iea[2].Trim();
            if (echoed != ControlNumber)
            {
                diagnostics.Add(new X12Diagnostic(
                    "X12-IEA02-CONTROL",
                    $"IEA02 (Interchange Control Number) is '{echoed}' but ISA13 is " +
                    $"'{ControlNumber}'. The trailer must echo the header control number exactly.",
                    _iea.Position));
            }
        }

        foreach (FunctionalGroup group in Groups)
        {
            group.Validate(diagnostics);
        }

        return diagnostics;
    }

    /// <summary>Renders a one-line summary, e.g. <c>ISA 000000001 SENDERID -&gt; RECEIVERID (00501, P) 1 group(s)</c>.</summary>
    public override string ToString() =>
        $"ISA {ControlNumber} {SenderId} -> {ReceiverId} ({VersionNumber}, {UsageIndicator}) {Groups.Count} group(s)";
}

/// <summary>
/// One functional group (GS/GE). A group holds transaction sets of a single type, all
/// bound for the same application at the receiver.
/// </summary>
public sealed class FunctionalGroup
{
    private readonly Segment _gs;
    private readonly Segment? _ge;

    internal FunctionalGroup(Segment gs, Segment? ge, IReadOnlyList<TransactionSet> transactions)
    {
        _gs = gs;
        _ge = ge;
        Transactions = transactions;
    }

    /// <summary>The raw GS segment.</summary>
    public Segment Header => _gs;

    /// <summary>The raw GE segment, or <see langword="null"/> if the sender omitted it.</summary>
    public Segment? Trailer => _ge;

    /// <summary>
    /// GS01, the functional identifier code. <c>QM</c> for 214 shipment status,
    /// <c>SM</c> for 204 load tender, <c>IM</c> for 210 invoice, <c>FA</c> for 997.
    /// </summary>
    public string FunctionalIdentifierCode => _gs[1].Trim();

    /// <summary>GS02, the application sender code. Distinct from ISA06 and frequently a different value.</summary>
    public string ApplicationSenderCode => _gs[2].Trim();

    /// <summary>GS03, the application receiver code.</summary>
    public string ApplicationReceiverCode => _gs[3].Trim();

    /// <summary>GS06, the group control number. GE02 must echo it.</summary>
    public string ControlNumber => _gs[6].Trim();

    /// <summary>
    /// GS08, the version/release/industry identifier code, e.g. <c>005010</c> or
    /// <c>004010VICS</c>. This, not ISA12, is what tells you which guide to map against.
    /// </summary>
    public string VersionReleaseIndustryCode => _gs[8].Trim();

    /// <summary>The transaction sets in this group.</summary>
    public IReadOnlyList<TransactionSet> Transactions { get; }

    internal void Validate(List<X12Diagnostic> diagnostics)
    {
        if (_ge is null)
        {
            diagnostics.Add(new X12Diagnostic(
                "X12-GE-MISSING",
                $"Functional group {ControlNumber} has no GE trailer segment.",
                _gs.Position));
        }
        else
        {
            string declared = _ge[1].Trim();
            if (declared != Transactions.Count.ToString(CultureInfo.InvariantCulture))
            {
                diagnostics.Add(new X12Diagnostic(
                    "X12-GE01-COUNT",
                    $"GE01 (Number of Transaction Sets Included) declares '{declared}', " +
                    $"group {ControlNumber} contains {Transactions.Count}.",
                    _ge.Position));
            }

            string echoed = _ge[2].Trim();
            if (echoed != ControlNumber)
            {
                diagnostics.Add(new X12Diagnostic(
                    "X12-GE02-CONTROL",
                    $"GE02 (Group Control Number) is '{echoed}' but GS06 is '{ControlNumber}'.",
                    _ge.Position));
            }
        }

        foreach (TransactionSet transaction in Transactions)
        {
            transaction.Validate(diagnostics);
        }
    }

    /// <summary>Renders a one-line summary.</summary>
    public override string ToString() =>
        $"GS {FunctionalIdentifierCode} {ControlNumber} ({VersionReleaseIndustryCode}) {Transactions.Count} transaction(s)";
}

/// <summary>
/// One transaction set (ST/SE) — a single business document, such as one 214 shipment
/// status message.
/// </summary>
public sealed class TransactionSet
{
    private readonly Segment _st;
    private readonly Segment? _se;

    internal TransactionSet(Segment st, Segment? se, IReadOnlyList<Segment> body)
    {
        _st = st;
        _se = se;
        Segments = body;
    }

    /// <summary>The raw ST segment.</summary>
    public Segment Header => _st;

    /// <summary>The raw SE segment, or <see langword="null"/> if the sender omitted it.</summary>
    public Segment? Trailer => _se;

    /// <summary>ST01, the transaction set identifier: <c>214</c>, <c>204</c>, <c>210</c>, <c>997</c>.</summary>
    public string IdentifierCode => _st[1].Trim();

    /// <summary>ST02, the transaction set control number. SE02 must echo it.</summary>
    public string ControlNumber => _st[2].Trim();

    /// <summary>
    /// ST03, the implementation convention reference, e.g. <c>005010X214</c>. Optional,
    /// added in 5010, and frequently absent.
    /// </summary>
    public string ImplementationConventionReference => _st[3].Trim();

    /// <summary>
    /// The body of the document: every segment between ST and SE, exclusive of both.
    /// </summary>
    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>
    /// The number of segments SE01 is supposed to declare: the body plus the ST and the SE
    /// themselves. Counting the trailer is the part people get wrong.
    /// </summary>
    public int DeclaredSegmentCount => Segments.Count + (_se is null ? 1 : 2);

    internal void Validate(List<X12Diagnostic> diagnostics)
    {
        if (_se is null)
        {
            diagnostics.Add(new X12Diagnostic(
                "X12-SE-MISSING",
                $"Transaction set {IdentifierCode} {ControlNumber} has no SE trailer segment.",
                _st.Position));
            return;
        }

        string declared = _se[1].Trim();
        string actual = DeclaredSegmentCount.ToString(CultureInfo.InvariantCulture);
        if (declared != actual)
        {
            diagnostics.Add(new X12Diagnostic(
                "X12-SE01-COUNT",
                $"SE01 (Number of Included Segments) declares '{declared}', transaction set " +
                $"{IdentifierCode} {ControlNumber} contains {actual} segments counting ST and SE.",
                _se.Position));
        }

        string echoed = _se[2].Trim();
        if (echoed != ControlNumber)
        {
            diagnostics.Add(new X12Diagnostic(
                "X12-SE02-CONTROL",
                $"SE02 (Transaction Set Control Number) is '{echoed}' but ST02 is '{ControlNumber}'.",
                _se.Position));
        }
    }

    /// <summary>Renders a one-line summary.</summary>
    public override string ToString() =>
        $"ST {IdentifierCode} {ControlNumber} ({Segments.Count} body segment(s))";
}
