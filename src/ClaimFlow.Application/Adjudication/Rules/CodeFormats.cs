using System.Text.RegularExpressions;

namespace ClaimFlow.Application.Adjudication.Rules;

public static partial class CodeFormats
{
    /// <summary>ICD-10-CM without the decimal point (X12 format), e.g. J069, E1165, S72001A.</summary>
    [GeneratedRegex("^[A-TV-Z][0-9][0-9A-Z][0-9A-Z]{0,4}$")]
    public static partial Regex Icd10Cm();

    /// <summary>CPT (5 digits, or 4 digits + F/T/U) or HCPCS Level II (letter + 4 digits).</summary>
    [GeneratedRegex("^([0-9]{4}[0-9FTU]|[A-V][0-9]{4})$")]
    public static partial Regex ProcedureCode();
}
