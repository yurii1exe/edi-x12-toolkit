using EdiX12.Core;

namespace EdiX12.Playground;

/// <summary>
/// Element names and code meanings taken from the published ANSI X12 specification, used
/// only to label what the parser found.
/// </summary>
/// <remarks>
/// <para>These are display labels, not a typed model. <c>EdiX12.Core</c> v0.1 parses the
/// envelope and gives you addressable segments; the typed 214 is v0.2. Nothing here is
/// derived from any partner's implementation guide.</para>
/// <para>When a name is unknown the UI shows the element reference (<c>AT705</c>) on its
/// own rather than guessing.</para>
/// </remarks>
public static class X12Reference
{
    private static readonly Dictionary<string, string[]> ElementNames = new(StringComparer.Ordinal)
    {
        ["ISA"] =
        [
            "Authorization Information Qualifier",
            "Authorization Information",
            "Security Information Qualifier",
            "Security Information",
            "Interchange Sender ID Qualifier",
            "Interchange Sender ID",
            "Interchange Receiver ID Qualifier",
            "Interchange Receiver ID",
            "Interchange Date (YYMMDD)",
            "Interchange Time (HHMM)",
            "Repetition Separator (5010) / Standards Identifier (4010)",
            "Interchange Control Version Number",
            "Interchange Control Number",
            "Acknowledgment Requested",
            "Interchange Usage Indicator",
            "Component Element Separator",
        ],
        ["GS"] =
        [
            "Functional Identifier Code",
            "Application Sender's Code",
            "Application Receiver's Code",
            "Date",
            "Time",
            "Group Control Number",
            "Responsible Agency Code",
            "Version / Release / Industry Identifier Code",
        ],
        ["ST"] =
        [
            "Transaction Set Identifier Code",
            "Transaction Set Control Number",
            "Implementation Convention Reference",
        ],
        ["SE"] =
        [
            "Number of Included Segments",
            "Transaction Set Control Number",
        ],
        ["GE"] =
        [
            "Number of Transaction Sets Included",
            "Group Control Number",
        ],
        ["IEA"] =
        [
            "Number of Included Functional Groups",
            "Interchange Control Number",
        ],
        ["B10"] =
        [
            "Reference Identification",
            "Shipment Identification Number",
            "Standard Carrier Alpha Code",
        ],
        ["LX"] =
        [
            "Assigned Number",
        ],
        ["AT7"] =
        [
            "Shipment Status Code",
            "Shipment Status or Appointment Reason Code",
            "Shipment Appointment Status Code",
            "Shipment Appointment Reason Code",
            "Date",
            "Time",
            "Time Code",
        ],
        ["MS1"] =
        [
            "City Name",
            "State or Province Code",
            "Country Code",
        ],
        ["MS2"] =
        [
            "Standard Carrier Alpha Code",
            "Equipment Number",
            "Equipment Description Code",
        ],
        ["L11"] =
        [
            "Reference Identification",
            "Reference Identification Qualifier",
            "Description",
        ],
    };

    private static readonly Dictionary<string, string> IdQualifiers = new(StringComparer.Ordinal)
    {
        ["01"] = "DUNS",
        ["02"] = "SCAC",
        ["08"] = "UCC EDI Communications ID",
        ["12"] = "Phone number",
        ["14"] = "DUNS plus suffix",
        ["ZZ"] = "Mutually defined",
    };

    private static readonly Dictionary<string, string> FunctionalIdentifiers = new(StringComparer.Ordinal)
    {
        ["QM"] = "Transportation Carrier Shipment Status (214)",
        ["SM"] = "Motor Carrier Load Tender (204)",
        ["IM"] = "Motor Carrier Freight Details and Invoice (210)",
        ["FA"] = "Functional Acknowledgment (997)",
    };

    private static readonly Dictionary<string, string> TransactionSets = new(StringComparer.Ordinal)
    {
        ["204"] = "Motor Carrier Load Tender",
        ["210"] = "Motor Carrier Freight Details and Invoice",
        ["214"] = "Transportation Carrier Shipment Status Message",
        ["990"] = "Response to a Load Tender",
        ["997"] = "Functional Acknowledgment",
    };

    /// <summary>The specification's name for an element, or <see langword="null"/> if not known here.</summary>
    public static string? ElementName(string segmentId, int elementNumber)
    {
        if (!ElementNames.TryGetValue(segmentId, out string[]? names))
        {
            return null;
        }

        return elementNumber >= 1 && elementNumber <= names.Length ? names[elementNumber - 1] : null;
    }

    /// <summary>What an ISA05/ISA07 interchange ID qualifier means, or <see langword="null"/>.</summary>
    public static string? IdQualifier(string code) =>
        IdQualifiers.TryGetValue(code, out string? name) ? name : null;

    /// <summary>What a GS01 functional identifier code means, or <see langword="null"/>.</summary>
    public static string? FunctionalIdentifier(string code) =>
        FunctionalIdentifiers.TryGetValue(code, out string? name) ? name : null;

    /// <summary>What an ST01 transaction set identifier means, or <see langword="null"/>.</summary>
    public static string? TransactionSetName(string code) =>
        TransactionSets.TryGetValue(code, out string? name) ? name : null;

    private static readonly Dictionary<string, (string Reference, string Description)> DiagnosticElements =
        new(StringComparer.Ordinal)
        {
            ["X12-IEA-MISSING"] = ("IEA", "interchange trailer absent"),
            ["X12-IEA01-COUNT"] = ("IEA01", "Number of Included Functional Groups"),
            ["X12-IEA02-CONTROL"] = ("IEA02", "Interchange Control Number"),
            ["X12-GE-MISSING"] = ("GE", "group trailer absent"),
            ["X12-GE01-COUNT"] = ("GE01", "Number of Transaction Sets Included"),
            ["X12-GE02-CONTROL"] = ("GE02", "Group Control Number"),
            ["X12-SE-MISSING"] = ("SE", "transaction set trailer absent"),
            ["X12-SE01-COUNT"] = ("SE01", "Number of Included Segments"),
            ["X12-SE02-CONTROL"] = ("SE02", "Transaction Set Control Number"),
        };

    /// <summary>
    /// The element a diagnostic code is about, so the table can show <c>SE01</c> beside
    /// <c>X12-SE01-COUNT</c> rather than making the reader decode the identifier.
    /// </summary>
    /// <param name="code">A code from <see cref="Interchange.Validate"/>.</param>
    /// <returns>
    /// The element reference and its specification name, or two <see langword="null"/>s
    /// for a code this table does not know — in which case the UI shows nothing rather
    /// than a guess.
    /// </returns>
    public static (string? Reference, string? Description) DiagnosticElement(string code) =>
        DiagnosticElements.TryGetValue(code, out (string Reference, string Description) e)
            ? (e.Reference, e.Description)
            : (null, null);
}
