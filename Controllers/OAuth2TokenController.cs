using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthSandbox.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthSandbox.Controllers;

/// <summary>
/// Simulates an OAuth2 authorization server token endpoint.
/// Accepts client_credentials grant type and returns a signed JWT.
/// </summary>
[ApiController]
[Route("oauth")]
[ApiExplorerSettings(GroupName = "v1-oauth2")]
public class OAuth2TokenController : ControllerBase
{
    private readonly SandboxCredentials _credentials;

    public OAuth2TokenController(IOptions<SandboxCredentials> credentials)
    {
        _credentials = credentials.Value;
    }

    /// <summary>
    /// OAuth2 token endpoint (application/x-www-form-urlencoded).
    /// </summary>
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public IActionResult Token([FromForm] OAuth2TokenRequest request)
    {
        if (request.grant_type != "client_credentials")
            return BadRequest(new { error = "unsupported_grant_type", error_description = "Only client_credentials is supported." });

        // Swagger UI sends client_id/client_secret as HTTP Basic Auth header.
        // Fall back to form body values if the header is absent.
        var (clientId, clientSecret) = ExtractClientCredentials(request);

        if (clientId != _credentials.OAuth2.ClientId || clientSecret != _credentials.OAuth2.ClientSecret)
            return Unauthorized(new { error = "invalid_client", error_description = "Invalid client_id or client_secret." });

        var scopes = string.IsNullOrWhiteSpace(request.scope)
            ? string.Join(" ", _credentials.OAuth2.Scopes.Keys)
            : request.scope;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("scope", scopes)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_credentials.Bearer.JwtKey));
        var signingCreds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _credentials.Bearer.Issuer,
            audience: _credentials.Bearer.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: signingCreds
        );

        return Ok(new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(token),
            token_type = "Bearer",
            expires_in = 3600,
            scope = scopes
        });
    }

    /// <summary>
    /// Extracts client credentials from the Basic Auth header first, then falls back to form body.
    /// Swagger UI sends them as: Authorization: Basic base64(client_id:client_secret)
    /// </summary>
    private (string clientId, string clientSecret) ExtractClientCredentials(OAuth2TokenRequest request)
    {
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var header = authHeader.ToString();
            if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
                    var parts = decoded.Split(':', 2);
                    if (parts.Length == 2)
                        return (parts[0], parts[1]);
                }
                catch { /* fall through to form values */ }
            }
        }

        return (request.client_id, request.client_secret);
    }
}

public class OAuth2TokenRequest
{
    public string grant_type { get; set; } = string.Empty;
    public string client_id { get; set; } = string.Empty;
    public string client_secret { get; set; } = string.Empty;
    public string? scope { get; set; }
}

