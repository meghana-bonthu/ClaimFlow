using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClaimFlow.Infrastructure.Persistence;
using Microsoft.IdentityModel.Tokens;

namespace ClaimFlow.Api.Auth;

public static class Roles
{
    public const string Submitter = "Submitter";
    public const string Adjudicator = "Adjudicator";
    public const string Admin = "Admin";
}

public static class Policies
{
    public const string SubmitClaims = nameof(SubmitClaims);
    public const string ReviewClaims = nameof(ReviewClaims);
    public const string ReleasePayments = nameof(ReleasePayments);
    public const string ViewAudit = nameof(ViewAudit);
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "ClaimFlow";
    public string Audience { get; set; } = "ClaimFlow.Web";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60;

    public SymmetricSecurityKey GetKey()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes. Set it via environment variable Jwt__SigningKey.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
    }
}

public sealed class TokenService(JwtOptions options, TimeProvider time)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(AppUser user)
    {
        var expires = time.GetUtcNow().AddMinutes(options.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            notBefore: time.GetUtcNow().UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(options.GetKey(), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class ClaimsPrincipalExtensions
{
    /// <summary>Actor recorded in audit entries and status history: the user's email, never PHI.</summary>
    public static string Actor(this ClaimsPrincipal user) =>
        user.FindFirstValue(JwtRegisteredClaimNames.Email) ?? user.FindFirstValue(ClaimTypes.Email) ?? "unknown";
}
