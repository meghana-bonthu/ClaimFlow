using ClaimFlow.Api.Auth;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Application.Abstractions;
using ClaimFlow.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ClaimFlow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    ClaimFlowDbContext db,
    IPasswordHasher<AppUser> hasher,
    TokenService tokens,
    IValidator<LoginRequest> validator,
    IAuditLog audit,
    IUnitOfWork unitOfWork) : ControllerBase
{
    /// <summary>Exchange demo credentials for a JWT bearer token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var result = user is null ? PasswordVerificationResult.Failed : hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            audit.Record("User", email, "LoginFailed", email);
            await unitOfWork.SaveChangesAsync(ct);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password");
        }

        audit.Record("User", email, "LoginSucceeded", email);
        await unitOfWork.SaveChangesAsync(ct);

        var (token, expires) = tokens.Issue(user!);
        return Ok(new LoginResponse(token, expires, user!.Email, user.DisplayName, user.Role));
    }
}
