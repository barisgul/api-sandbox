using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AuthSandbox.Infrastructure;


public enum ApiKeyLocation { Header, Query, Cookie }

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public ApiKeyLocation Location { get; set; } = ApiKeyLocation.Header;
    public string KeyName { get; set; } = "X-Api-Key";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly SandboxCredentials _credentials;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsSnapshot<SandboxCredentials> credentials)
        : base(options, logger, encoder)
    {
        _credentials = credentials.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? providedApiKey = null;

        providedApiKey = Options.Location switch
        {
            ApiKeyLocation.Header when Request.Headers.TryGetValue(Options.KeyName, out var h)
                => h.FirstOrDefault(),
            ApiKeyLocation.Query when Request.Query.TryGetValue(Options.KeyName, out var q)
                => q.FirstOrDefault(),
            ApiKeyLocation.Cookie when Request.Cookies.TryGetValue(Options.KeyName, out var c)
                => c,
            _ => null
        };

        // Resolve the correct expected key based on which transport location is configured
        var expectedKey = Options.Location switch
        {
            ApiKeyLocation.Header => _credentials.ApiKeyHeader.Key,
            ApiKeyLocation.Query  => _credentials.ApiKeyQuery.Key,
            ApiKeyLocation.Cookie => _credentials.ApiKeyCookie.Key,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(providedApiKey))
            return Task.FromResult(AuthenticateResult.Fail($"API Key not provided in {Options.Location}."));

        if (providedApiKey != expectedKey)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API Key."));

        var claims = new[] 
        { 
            new Claim(ClaimTypes.Name, $"ApiKeyUser-{Options.Location}"),
            new Claim(ClaimTypes.NameIdentifier, $"ApiKeyUser-{Options.Location}")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var reason = Context.Features.Get<Microsoft.AspNetCore.Authentication.IAuthenticateResultFeature>()
            ?.AuthenticateResult?.Failure?.Message
            ?? $"API Key missing or invalid ({Options.Location}).";

        await AuthErrorResponse.Write401Async(Response, Scheme.Name, reason);
    }
}
