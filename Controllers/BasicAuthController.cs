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

    /// <summary>
    /// Additional test endpoint (Test 2) secured by Basic authentication
    /// </summary>
    [HttpGet("test-2")]
    public IActionResult GetTest2()
    {
        return Ok(new
        {
            Message = "Test 2 endpoint successfully accessed via Basic authentication.",
            User = User.Identity?.Name,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Additional test endpoint (Test 2) secured by Basic authentication
    /// </summary>
    [HttpGet("test-3")]
    public IActionResult GetTest3()
    {
        return Ok(new
        {
            Message = "Test 3 endpoint successfully accessed via Basic authentication.",
            User = User.Identity?.Name,
            Timestamp = DateTime.UtcNow
        });
    }
}
