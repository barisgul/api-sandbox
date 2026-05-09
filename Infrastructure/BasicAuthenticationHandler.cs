using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AuthSandbox.Infrastructure;

public class BasicAuthenticationOptions : AuthenticationSchemeOptions { }

public class BasicAuthenticationHandler : AuthenticationHandler<BasicAuthenticationOptions>
{
    private readonly SandboxCredentials _credentials;

    public BasicAuthenticationHandler(
        IOptionsMonitor<BasicAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsSnapshot<SandboxCredentials> credentials)
        : base(options, logger, encoder)
    {
        _credentials = credentials.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("Missing Authorization Header"));

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"].ToString());

            if (!string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Scheme"));

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var decoded = Encoding.UTF8.GetString(credentialBytes);
            var parts = decoded.Split(':', 2);
            
            if (parts.Length != 2)
                return Task.FromResult(AuthenticateResult.Fail("Invalid Basic Authentication format."));

            var username = parts[0];
            var password = parts[1];

            // Explicit validation against config
            if (username != _credentials.Basic.Username || password != _credentials.Basic.Password)
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid Username or Password"));
            }

            var claims = new[] 
            { 
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.NameIdentifier, username)
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Authorization Header Format"));
        }
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var reason = Context.Features.Get<Microsoft.AspNetCore.Authentication.IAuthenticateResultFeature>()
            ?.AuthenticateResult?.Failure?.Message
            ?? "Missing or invalid Basic credentials.";

        Response.Headers.WWWAuthenticate = "Basic realm=\"SandboxAPI\"";
        await AuthErrorResponse.Write401Async(Response, Scheme.Name, reason);
    }
}
