using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingRisk.Api.Contracts;
using TradingRisk.Api.Security;

namespace TradingRisk.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(DemoTokenService tokenService, IHostEnvironment environment)
    : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("token")]
    public ActionResult<DemoTokenResponse> CreateDemoToken(DemoTokenRequest request)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.UserName) ||
            request.Role is not ("risk-reader" or "risk-operator"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid demo token request",
                Detail = "Use a non-empty user name and role risk-reader or risk-operator."
            });
        }

        return Ok(new DemoTokenResponse(
            tokenService.Create(request.UserName.Trim(), request.Role),
            "Bearer",
            1_800));
    }
}
