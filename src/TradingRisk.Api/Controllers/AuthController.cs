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
    public ActionResult<DemoTokenResponse> CreateDemoToken(
        DemoTokenRequest request,
        [FromServices] CredentialValidator credentials)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        if (!credentials.TryValidate(request.UserName, request.Password, out var user))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid credentials",
                Detail = "The user name or password is incorrect. Try again later if the account is locked."
            });
        }

        return Ok(new DemoTokenResponse(
            tokenService.Create(user!.UserName, user.Role),
            "Bearer",
            1_800,
            user.UserName,
            user.Role));
    }
}
