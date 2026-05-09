using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using AuthSandbox.Infrastructure;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/validate")]
[ApiExplorerSettings(IgnoreApi = true)] // Hide from Swagger to keep it clean
public class ValidationController : ControllerBase
{
    private readonly SandboxCredentials _creds;

    public ValidationController(IOptionsSnapshot<SandboxCredentials> creds)
    {
        _creds = creds.Value;
    }

    [HttpPost("basic")]
    public IActionResult ValidateBasic([FromForm] string username, [FromForm] string password)
    {
        if (username == _creds.Basic.Username && password == _creds.Basic.Password)
            return Ok();
        return Unauthorized(new { error = "Invalid credentials" });
    }

    [HttpPost("apikey")]
    public IActionResult ValidateApiKey([FromForm] string key, [FromForm] string location)
    {
        string expected = location.ToLower() switch
        {
            "header" => _creds.ApiKeyHeader.Key,
            "query" => _creds.ApiKeyQuery.Key,
            "cookie" => _creds.ApiKeyCookie.Key,
            _ => ""
        };

        if (!string.IsNullOrEmpty(expected) && key == expected)
            return Ok();

        return Unauthorized(new { error = "Invalid API Key" });
    }

    [HttpPost("bearer")]
    public IActionResult ValidateBearer([FromForm] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { error = "Token is required" });

        try
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_creds.Bearer.JwtKey));
            new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _creds.Bearer.Issuer,
                ValidAudience = _creds.Bearer.Audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.Zero
            }, out _);
            return Ok();
        }
        catch
        {
            return Unauthorized(new { error = "Invalid or expired token" });
        }
    }
}
