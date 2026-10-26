using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ClaimFlow.Api.Auth;
using ClaimFlow.Api.Contracts;
using ClaimFlow.Api.Middleware;
using ClaimFlow.Api.Startup;
using ClaimFlow.Application;
using ClaimFlow.Application.Adjudication;
using ClaimFlow.Infrastructure;
using ClaimFlow.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Application + infrastructure ----------
var adjudicationOptions = config.GetSection("Adjudication").Get<AdjudicationOptions>() ?? new AdjudicationOptions();
builder.Services.AddClaimFlowApplication(adjudicationOptions);
builder.Services.AddClaimFlowInfrastructure(config, runWorker: config.GetValue("Worker:Enabled", true));
builder.Services.AddSingleton(TimeProvider.System);

// ---------- Auth ----------
var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
var signingKey = jwt.GetKey(); // fail fast at startup if the key is missing or too short
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = "name"
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.SubmitClaims, p => p.RequireRole(Roles.Submitter, Roles.Admin))
    .AddPolicy(Policies.ReviewClaims, p => p.RequireRole(Roles.Adjudicator, Roles.Admin))
    .AddPolicy(Policies.ViewAudit, p => p.RequireRole(Roles.Adjudicator, Roles.Admin))
    .AddPolicy(Policies.ReleasePayments, p => p.RequireRole(Roles.Admin));

// ---------- Web ----------
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddValidatorsFromAssemblyContaining<SubmitClaimRequestValidator>();
builder.Services.AddProblemDetails();

var loginPermitLimit = config.GetValue("RateLimiting:LoginPermitsPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginPermitLimit, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders(CorrelationIdMiddleware.Header, "Content-Disposition")));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ClaimFlow API",
        Version = "v1",
        Description = "Healthcare claims intake, rules-based adjudication, pricing and remittance."
    });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the token from POST /api/auth/login"
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "ClaimFlow.Api.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<ProblemDetailsMappingMiddleware>();

if (config.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "ClaimFlow API");
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

await DatabaseInitializer.InitializeAsync(app.Services, config);
await app.RunAsync();

public partial class Program;
