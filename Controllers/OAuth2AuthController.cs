using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiExplorerSettings(GroupName = "v1-oauth2")]
public class OAuth2AuthController : ControllerBase
{
    [HttpGet("secure-data")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by OAuth2 authentication (JWT validation).", User = User.FindFirstValue(ClaimTypes.NameIdentifier) });
    }
}
