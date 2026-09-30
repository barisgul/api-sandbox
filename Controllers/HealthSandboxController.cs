using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthSandbox.Controllers
{
    [ApiController]
    [Route("api/sandbox/health")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "v1-health")]
    public class HealthSandboxController : ControllerBase
    {
        /// <summary>
        /// Check sandbox service health and operational status
        /// </summary>
        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new
            {
                status = "Healthy",
                service = "Quixa Test API Sandbox Message",
                version = "1.0.0",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
