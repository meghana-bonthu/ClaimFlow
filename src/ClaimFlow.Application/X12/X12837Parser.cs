using System.Globalization;

namespace ClaimFlow.Application.X12;

public sealed record ParsedServiceLine(string ProcedureCode, string? Modifier, decimal Charge, int Units, DateOnly? ServiceDate);

public sealed class ParsedClaim
{
    public string? BillingProviderNpi { get; set; }
    public string? MemberNumber { get; set; }
    public string? PatientControlNumber { get; set; }
    public decimal TotalCharge { get; set; }
    public List<string> DiagnosisCodes { get; } = [];
    public List<ParsedServiceLine> Lines { get; } = [];
    public List<string> Errors { get; } = [];
    internal DateOnly? ClaimLevelServiceDate { get; set; }
}

public sealed record X12ParseResult(string? InterchangeControlNumber, IReadOnlyList<ParsedClaim> Claims, IReadOnlyList<string> Errors);

/// <summary>
/// Parses the parts of an ASC X12 837P (005010X222A1) professional claim file that ClaimFlow needs.
/// Delimiters are read from the fixed-width ISA header, so files using non-default separators work.
/// Supported loops: 2010AA billing provider (NM1*85), 2010BA subscriber (NM1*IL), 2300 claim (CLM, HI, DTP*472),
/// 2400 service line (LX, SV1, DTP*472). Multiple claims per file are supported.
/// </summary>
public static class X12837Parser
{
    private const int IsaLength = 106;

    public static X12ParseResult Parse(string content)
    {
        var errors = new List<string>();
        var claims = new List<ParsedClaim>();

        content = content.TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (content.Length < IsaLength || !content.StartsWith("ISA", StringComparison.Ordinal))
            return new(null, claims, ["File does not start with a valid ISA interchange header."]);

        var elementSep = content[3];
        var componentSep = content[104];
        var segmentTerm = content[105];

        var segments = content
            .Split(segmentTerm)
            .Select(s => s.Trim('\r', '\n', ' '))
            .Where(s => s.Length > 0)
            .Select(s => s.Split(elementSep))
            .ToList();

        var isa = segments[0];
        var controlNumber = isa.Length > 13 ? isa[13].Trim() : null;

        if (!segments.Any(s => s[0] == "ST" && El(s, 1) == "837"))
            errors.Add("No ST*837 transaction set found.");

        string? billingNpi = null;
        string? memberNumber = null;
        string? currentEntity = null; // last NM1 entity code, so DTP/REF can be scoped
        ParsedClaim? claim = null;
        var lineCharge = 0m;
        var lineUnits = 0;
        string? lineCode = null, lineModifier = null;
        var inLine = false;

        void FlushLine(DateOnly? date)
        {
            if (claim is null || lineCode is null) return;
            claim.Lines.Add(new ParsedServiceLine(lineCode, lineModifier, lineCharge, lineUnits, date));
            lineCode = null;
        }

        DateOnly? pendingLineDate = null;

        void CloseLine()
        {
            if (inLine)
            {
                FlushLine(pendingLineDate ?? claim?.ClaimLevelServiceDate);
                inLine = false;
                pendingLineDate = null;
            }
        }

        void CloseClaim()
        {
            CloseLine();
            if (claim is null) return;
            Validate(claim);
            claims.Add(claim);
            claim = null;
        }

        foreach (var seg in segments)
        {
            switch (seg[0])
            {
                case "NM1":
                    currentEntity = El(seg, 1);
                    if (currentEntity == "85")
                    {
                        CloseClaim();
                        billingNpi = El(seg, 8) == "XX" ? El(seg, 9) : null;
                    }
                    else if (currentEntity == "IL")
                    {
                        CloseClaim();
                        memberNumber = El(seg, 9);
                    }
                    break;

                case "CLM":
                    CloseClaim();
                    claim = new ParsedClaim
                    {
                        BillingProviderNpi = billingNpi,
                        MemberNumber = memberNumber,
                        PatientControlNumber = El(seg, 1),
                        TotalCharge = Dec(El(seg, 2))
                    };
                    break;

                case "HI" when claim is not null:
                    foreach (var element in seg.Skip(1))
                    {
                        var parts = element.Split(componentSep);
                        if (parts.Length >= 2 && parts[0] is "ABK" or "ABF" or "BK" or "BF" && parts[1].Length > 0)
                            claim.DiagnosisCodes.Add(parts[1]);
                    }
                    break;

                case "LX" when claim is not null:
                    CloseLine();
                    inLine = true;
                    break;

                case "SV1" when claim is not null && inLine:
                    var proc = El(seg, 1).Split(componentSep);
                    lineCode = proc.Length > 1 ? proc[1] : null;
                    lineModifier = proc.Length > 2 && proc[2].Length > 0 ? proc[2] : null;
                    lineCharge = Dec(El(seg, 2));
                    lineUnits = (int)Math.Round(Dec(El(seg, 4)), MidpointRounding.AwayFromZero);
                    if (lineCode is null) claim.Errors.Add("SV1 segment is missing a procedure code.");
                    break;

                case "DTP" when claim is not null && El(seg, 1) == "472":
                    var date = Date(El(seg, 2), El(seg, 3));
                    if (date is null) claim.Errors.Add($"Invalid service date '{El(seg, 3)}'.");
                    if (inLine) pendingLineDate = date;
                    else claim.ClaimLevelServiceDate = date;
                    break;

                case "SE":
                case "GE":
                case "IEA":
                    CloseClaim();
                    break;
            }
        }

        CloseClaim();
        if (claims.Count == 0 && errors.Count == 0) errors.Add("No CLM (claim) segments found.");
        return new(controlNumber, claims, errors);
    }

    private static void Validate(ParsedClaim claim)
    {
        if (string.IsNullOrWhiteSpace(claim.BillingProviderNpi)) claim.Errors.Add("Billing provider NPI (NM1*85 ... XX) is missing.");
        if (string.IsNullOrWhiteSpace(claim.MemberNumber)) claim.Errors.Add("Subscriber member ID (NM1*IL ... MI) is missing.");
        if (claim.DiagnosisCodes.Count == 0) claim.Errors.Add("No diagnosis codes (HI segment) found.");
        if (claim.Lines.Count == 0) claim.Errors.Add("No service lines (LX/SV1) found.");
        if (claim.Lines.Any(l => l.ServiceDate is null)) claim.Errors.Add("One or more service lines has no service date (DTP*472).");

        var lineTotal = claim.Lines.Sum(l => l.Charge);
        if (claim.Lines.Count > 0 && lineTotal != claim.TotalCharge)
            claim.Errors.Add($"CLM02 total charge {claim.TotalCharge:0.00} does not equal sum of service lines {lineTotal:0.00}.");
    }

    private static string El(string[] seg, int i) => i < seg.Length ? seg[i].Trim() : string.Empty;

    private static decimal Dec(string s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static DateOnly? Date(string format, string value)
    {
        // D8 = single date, RD8 = range (use the start date)
        var text = format == "RD8" ? value.Split('-')[0] : value;
        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
