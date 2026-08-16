namespace EdiX12.Tests;

/// <summary>
/// Test interchanges, written by hand from the public X12 specification.
/// </summary>
/// <remarks>
/// <para>
/// Every identifier here is invented. <c>DEMOSENDER</c>, <c>DEMORECEIVER</c> and the SCAC
/// <c>DEMO</c> are not real trading partners, and no part of these files is derived from
/// any production interchange. That matters for more than tidiness: EDI fixtures are the
/// easiest way to leak a partner relationship into a public repository.
/// </para>
/// <para>
/// Line endings are written explicitly rather than using multi-line string literals,
/// because whether a segment terminator is followed by a newline is exactly the thing
/// under test, and a text editor normalising the file would silently change the fixture.
/// </para>
/// </remarks>
internal static class Fixtures
{
    /// <summary>
    /// A spec-compliant ISA, 106 characters, delimiters <c>*</c> <c>:</c> <c>~</c> and a
    /// repetition separator of <c>^</c> in ISA11 (valid because ISA12 is 00501).
    /// ISA15 is <c>T</c>: these are test interchanges and say so.
    /// </summary>
    internal const string IsaStandard =
        "ISA*00*          *00*          *ZZ*DEMOSENDER     *ZZ*DEMORECEIVER   " +
        "*260815*1430*^*00501*000000001*0*T*:~";

    /// <summary>
    /// The same interchange header with entirely different delimiters: <c>|</c> for
    /// elements, <c>&gt;</c> for components, <c>!</c> for repetitions, and a newline as
    /// the segment terminator. All legal, all in live use, and all fatal to a parser that
    /// assumes <c>*</c> and <c>~</c>.
    /// </summary>
    internal const string IsaPipeNewline =
        "ISA|00|          |00|          |ZZ|DEMOSENDER     |ZZ|DEMORECEIVER   " +
        "|260815|1430|!|00501|000000001|0|T|>\n";

    /// <summary>
    /// A 4010 ISA. ISA11 holds <c>U</c>, the Interchange Control Standards Identifier —
    /// not a repetition separator. Reading it as one is a version-detection bug.
    /// </summary>
    internal const string Isa4010 =
        "ISA*00*          *00*          *ZZ*DEMOSENDER     *ZZ*DEMORECEIVER   " +
        "*260815*1430*U*00401*000000001*0*T*:~";

    /// <summary>
    /// A defective ISA: ISA06 is padded to 14 characters instead of 15, so the segment is
    /// 105 characters and every fixed offset after ISA06 is shifted by one. Senders do
    /// this. A parser that trusts offset 104 blindly will read <c>T</c> as the component
    /// separator and <c>*</c> as the segment terminator, and fail somewhere else entirely.
    /// </summary>
    internal const string IsaNarrowSenderId =
        "ISA*00*          *00*          *ZZ*DEMOSENDER    *ZZ*DEMORECEIVER   " +
        "*260815*1430*^*00501*000000001*0*T*:~";

    /// <summary>
    /// A minimal but complete 214 (Transportation Carrier Shipment Status Message):
    /// one shipment, one status event, no line-item detail.
    /// <code>
    /// GS01 = QM      functional group for 214
    /// B1002          shipment identification number
    /// B1003          SCAC of the reporting carrier
    /// AT701 = AF     "carrier departed pick-up location with shipment"
    /// AT702 = NS     "normal appointment status"
    /// AT705 / AT706  event date CCYYMMDD and time HHMM
    /// AT707 = LT     local time
    /// MS1            city / state / country where the event happened
    /// MS2            SCAC and equipment number
    /// </code>
    /// SE01 is 7: ST, B10, LX, AT7, MS1, MS2, SE. Counting the SE itself is the part
    /// that gets miscounted, and a wrong SE01 gets the whole interchange rejected.
    /// </summary>
    private static readonly string[] Body214 =
    {
        "GS*QM*DEMOAPPSEND*DEMOAPPRECV*20260815*1430*1*X*005010",
        "ST*214*0001",
        "B10*4938*SHIPMENT001*DEMO",
        "LX*1",
        "AT7*AF*NS***20260815*1430*LT",
        "MS1*ATLANTA*GA*US",
        "MS2*DEMO*TRAILER123",
        "SE*7*0001",
        "GE*1*1",
        "IEA*1*000000001",
    };

    /// <summary>The whole 214 on one line, no line endings anywhere. Common from automated senders.</summary>
    internal static string Standard214OneLine => IsaStandard + string.Join("~", Body214) + "~";

    /// <summary>
    /// The same 214 with a CRLF after every segment terminator. Equally common, from
    /// senders that write files a line at a time. It must parse identically.
    /// </summary>
    internal static string Standard214CrLf =>
        IsaStandard + "\r\n" + string.Join("~\r\n", Body214) + "~\r\n";

    /// <summary>The same 214 with a bare LF after every terminator.</summary>
    internal static string Standard214Lf =>
        IsaStandard + "\n" + string.Join("~\n", Body214) + "~\n";

    /// <summary>
    /// The same 214, byte for byte in meaning, but delimited with <c>|</c> and terminated
    /// with a newline. Parsing this into the same object graph as
    /// <see cref="Standard214OneLine"/> is the whole point of reading delimiters from the ISA.
    /// </summary>
    internal static string Pipe214 =>
        IsaPipeNewline + string.Join("\n", Array.ConvertAll(Body214, s => s.Replace('*', '|'))) + "\n";

    /// <summary>
    /// A 214 whose SE01 declares 9 segments when the transaction set contains 7. This is
    /// the single most common reason a partner rejects an otherwise correct file.
    /// </summary>
    internal static string Standard214WrongSegmentCount =>
        Standard214OneLine.Replace("SE*7*0001~", "SE*9*0001~");

    /// <summary>
    /// A 214 whose IEA02 does not echo ISA13. The receiver cannot reconcile the
    /// interchange and will usually reject it without saying why.
    /// </summary>
    internal static string Standard214WrongInterchangeControlNumber =>
        Standard214OneLine.Replace("IEA*1*000000001~", "IEA*1*000000002~");
}
