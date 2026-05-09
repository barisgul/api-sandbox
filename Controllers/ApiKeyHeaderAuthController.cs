using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKeyHeader")]
[ApiExplorerSettings(GroupName = "v1-apikey-header")]
public class ApiKeyHeaderAuthController : ControllerBase
{
    [HttpGet("secure-data")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by API Key (Header) authentication.", User = User.Identity?.Name });
    }
}
