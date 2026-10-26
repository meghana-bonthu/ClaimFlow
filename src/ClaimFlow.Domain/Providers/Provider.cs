using ClaimFlow.Domain.Common;

namespace ClaimFlow.Domain.Providers;

public sealed class Provider : Entity
{
    private Provider() { }

    public Provider(string npi, string name, string specialty, bool isInNetwork, bool isActive = true)
    {
        if (!NpiValidator.IsValid(npi)) throw new DomainException($"'{npi}' is not a valid NPI.");
        Npi = npi;
        Name = name;
        Specialty = specialty;
        IsInNetwork = isInNetwork;
        IsActive = isActive;
    }

    public string Npi { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Specialty { get; private set; } = default!;
    public bool IsInNetwork { get; private set; }
    public bool IsActive { get; private set; }

    public void Deactivate() => IsActive = false;
}

/// <summary>
/// Validates a National Provider Identifier: 10 digits with a Luhn check digit computed over the
/// "80840" health-industry prefix plus the first 9 digits (per CMS NPI check digit specification).
/// </summary>
public static class NpiValidator
{
    public static bool IsValid(string? npi)
    {
        if (npi is null || npi.Length != 10 || !npi.All(char.IsAsciiDigit)) return false;

        var payload = "80840" + npi[..9];
        var sum = 0;
        var doubleIt = true; // rightmost payload digit is doubled because the check digit sits to its right
        for (var i = payload.Length - 1; i >= 0; i--)
        {
            var d = payload[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }

        var check = (10 - sum % 10) % 10;
        return check == npi[9] - '0';
    }
}
