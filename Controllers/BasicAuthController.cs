using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(AuthenticationSchemes = "Basic")]
[ApiExplorerSettings(GroupName = "v1-basic")]
public class BasicAuthController : ControllerBase
{
    [HttpGet("secure-data")]
    public IActionResult GetSecureData()
    {
        return Ok(new { Message = "This data is secured by Basic authentication.", User = User.Identity?.Name });
    }
}
