using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKeyCookie")]
[ApiExplorerSettings(GroupName = "v1-apikey-cookie")]

public class ApiKeyCookieAuthController : ControllerBase
{
    [HttpGet("secure-data")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by API Key (Cookie) authentication.", User = User.Identity?.Name });
        
    }
}
