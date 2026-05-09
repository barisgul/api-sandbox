using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[ApiExplorerSettings(GroupName = "v1-oidc")]
public class OidcAuthController : ControllerBase
{
    [HttpGet("secure-data")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by OpenID Connect (JWT validation).", User = User.FindFirstValue(ClaimTypes.NameIdentifier) });
    }
}
