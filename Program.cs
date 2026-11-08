using System.Globalization;
using System.Text;
using AuthSandbox.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using AuthSandbox.Infrastructure.Demo;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Parse decimals and dates the same way on every host (form fields such as "1850.00").
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Bind SandboxCredentials from configuration
builder.Services.Configure<SandboxCredentials>(
    builder.Configuration.GetSection(SandboxCredentials.Section));

var sandboxCreds = builder.Configuration
    .GetSection(SandboxCredentials.Section)
    .Get<SandboxCredentials>() ?? new SandboxCredentials();

// Add services to the container.
builder.Services.Configure<DemoAuthOptions>(builder.Configuration.GetSection(DemoAuthOptions.Section));
builder.Services.AddControllers(o => o.Filters.Add<DemoAuthFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.TryAddSingleton(TimeProvider.System);

// One in-memory state singleton per demo domain (BankingState, TravelState, ...)
foreach (var stateType in typeof(SandboxState).Assembly.GetTypes()
             .Where(t => t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(SandboxState))))
    builder.Services.AddSingleton(stateType);


// Configure Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyHeader", options =>
{
    options.Location = ApiKeyLocation.Header;
    options.KeyName = "X-Api-Key";
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyQuery", options =>
{
    options.Location = ApiKeyLocation.Query;
    options.KeyName = "api_key";
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKeyCookie", options =>
{
    options.Location = ApiKeyLocation.Cookie;
    options.KeyName = "AuthCookie";
})
.AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>("Basic", options => { })
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = sandboxCreds.Bearer.Issuer,
        ValidAudience = sandboxCreds.Bearer.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sandboxCreds.Bearer.JwtKey))
    };

    options.Events = new JwtBearerEvents
    {
        OnChallenge = async ctx =>
        {
            ctx.HandleResponse();
            var reason = ctx.AuthenticateFailure?.Message ?? "Missing or invalid Bearer token.";
            ctx.Response.ContentType = "application/json";
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;

            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = 401,
                error = "Unauthorized",
                scheme = "Bearer",
                reason
            }, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            await ctx.Response.WriteAsync(body);
        }
    };
});

builder.Services.AddSandboxSwagger(sandboxCreds);

var app = builder.Build();

// Enable Swagger in all environments for sandbox usage
app.UseSandboxSwaggerUi();

app.UseStaticFiles();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Quixa Test API Sandbox",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
})).ExcludeFromDescription();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "Quixa Test API Sandbox",
    version = "1.0.0",
    timestamp = DateTime.UtcNow
})).ExcludeFromDescription();

app.Run();

public partial class Program;
