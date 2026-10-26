using System.Globalization;
using System.Text;
using ClaimFlow.Domain.Claims;
using ClaimFlow.Domain.Members;
using ClaimFlow.Domain.Providers;

namespace ClaimFlow.Application.X12;

/// <summary>
/// Builds a simplified ASC X12 835 (005010X221A1) Electronic Remittance Advice for a single adjudicated claim,
/// including CAS adjustment segments (CO = contractual, PR = patient responsibility) per service line.
/// </summary>
public static class X12835Builder
{
    public const string PayerName = "CLAIMFLOW HEALTH PLAN";
    public const string PayerId = "CLAIMFLOW";

    public static string Build(Claim claim, Member member, Provider provider, DateTimeOffset generatedAt)
    {
        if (claim.Status is not (ClaimStatus.Approved or ClaimStatus.Denied or ClaimStatus.Paid))
            throw new InvalidOperationException("A remittance can only be produced for an adjudicated claim.");

        var date = generatedAt.UtcDateTime;
        var control = (BitConverter.ToUInt32(claim.Id.ToByteArray(), 0) % 1_000_000_000).ToString("D9", CultureInfo.InvariantCulture);
        var trace = claim.PaymentReference ?? claim.ClaimNumber;
        var body = new List<string>();

        body.Add("ST*835*0001");
        body.Add($"BPR*{(claim.PlanPaid > 0 ? "I" : "H")}*{Amt(claim.PlanPaid)}*C*{(claim.PlanPaid > 0 ? "ACH" : "NON")}************{date:yyyyMMdd}");
        body.Add($"TRN*1*{trace}*1{PayerId}");
        body.Add($"DTM*405*{date:yyyyMMdd}");
        body.Add($"N1*PR*{PayerName}");
        body.Add($"N1*PE*{Clean(provider.Name)}*XX*{provider.Npi}");
        body.Add("LX*1");

        // CLP02 status: 1 = processed as primary, 4 = denied
        var status = claim.Status == ClaimStatus.Denied ? "4" : "1";
        body.Add($"CLP*{claim.PatientControlNumber ?? claim.ClaimNumber}*{status}*{Amt(claim.TotalBilled)}*{Amt(claim.PlanPaid)}*{Amt(claim.MemberResponsibility)}*12*{claim.ClaimNumber}");
        body.Add($"NM1*QC*1*{Clean(member.LastName)}*{Clean(member.FirstName)}****MI*{member.MemberNumber}");

        foreach (var line in claim.Lines)
        {
            var proc = line.Modifier is null ? $"HC:{line.ProcedureCode}" : $"HC:{line.ProcedureCode}:{line.Modifier}";
            body.Add($"SVC*{proc}*{Amt(line.BilledAmount)}*{Amt(line.PlanPaid)}**{line.Units}");
            body.Add($"DTM*472*{line.ServiceDate:yyyyMMdd}");

            if (line.Status == ClaimLineStatus.Denied)
            {
                body.Add($"CAS*CO*{line.DenialCode}*{Amt(line.BilledAmount)}");
                continue;
            }

            var contractual = line.BilledAmount - line.AllowedAmount;
            if (contractual > 0) body.Add($"CAS*CO*45*{Amt(contractual)}");
            if (line.DeductibleAmount > 0) body.Add($"CAS*PR*1*{Amt(line.DeductibleAmount)}");
            if (line.CoinsuranceAmount > 0) body.Add($"CAS*PR*2*{Amt(line.CoinsuranceAmount)}");
            if (line.CopayAmount > 0) body.Add($"CAS*PR*3*{Amt(line.CopayAmount)}");
            body.Add($"AMT*B6*{Amt(line.AllowedAmount)}");
        }

        body.Add($"SE*{body.Count + 1}*0001");

        var sb = new StringBuilder();
        sb.Append($"ISA*00*          *00*          *ZZ*{PayerId,-15}*ZZ*{provider.Npi,-15}*{date:yyMMdd}*{date:HHmm}*^*00501*{control}*0*P*:~\n");
        sb.Append($"GS*HP*{PayerId}*{provider.Npi}*{date:yyyyMMdd}*{date:HHmm}*1*X*005010X221A1~\n");
        foreach (var segment in body) sb.Append(segment).Append("~\n");
        sb.Append("GE*1*1~\n");
        sb.Append($"IEA*1*{control}~\n");
        return sb.ToString();
    }

    private static string Amt(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Clean(string value) =>
        new string(value.ToUpperInvariant().Where(c => c is not ('*' or '~' or ':' or '^')).ToArray());
}
