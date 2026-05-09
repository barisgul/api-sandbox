using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthSandbox.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiExplorerSettings(GroupName = "v1-bearer")]
public class BearerAuthController : ControllerBase
{
    private readonly SandboxCredentials _credentials;

    public BearerAuthController(IOptions<SandboxCredentials> credentials)
    {
        _credentials = credentials.Value;
    }

    /// <summary>Generate a sandbox JWT for testing Bearer endpoints.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    public IActionResult GenerateToken()
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "sandbox-bearer-user"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_credentials.Bearer.JwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _credentials.Bearer.Issuer,
            audience: _credentials.Bearer.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return Ok(new
        {
            access_token = new JwtSecurityTokenHandler().WriteToken(token),
            expires_in = 3600,
            token_type = "Bearer"
        });
    }

    [HttpGet("secure-data")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by JWT Bearer authentication.", User = User.FindFirstValue(ClaimTypes.NameIdentifier) });
    }
}
