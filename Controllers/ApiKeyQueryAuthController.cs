using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "ApiKeyQuery")]
[ApiExplorerSettings(GroupName = "v1-apikey-query")]
public class ApiKeyQueryAuthController : ControllerBase
{
    [HttpGet("secure-data")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by API Key (Query) authentication.", User = User.Identity?.Name });
    }
}
