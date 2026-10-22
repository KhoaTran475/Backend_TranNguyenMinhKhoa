using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AuthService auth, UserManager<ApplicationUser> users) : ControllerBase
{
    [HttpPost("register"), EnableRateLimiting("otp")][ProducesResponseType(typeof(ResponseEnvelope<ChallengeView>), 201)] public async Task<IActionResult> Register(RegisterRequest r) => StatusCode(201, ApiResponse.Ok(await auth.Register(r), "Tài khoản đã tạo; hãy xác minh email."));
    [HttpPost("login"), EnableRateLimiting("otp")][ProducesResponseType(typeof(ResponseEnvelope<ChallengeView>), 200)] public async Task<IActionResult> Login(LoginRequest r) => Ok(ApiResponse.Ok(await auth.Login(r), "Đã gửi OTP; chưa cấp JWT."));
    [HttpPost("resend-otp"), EnableRateLimiting("otp")][ProducesResponseType(typeof(ResponseEnvelope<ChallengeView>), 200)] public async Task<IActionResult> Resend(ResendRequest r) => Ok(ApiResponse.Ok(await auth.Resend(r.ChallengeId)));
    [HttpPost("verify-email"), EnableRateLimiting("verify")][ProducesResponseType(typeof(ResponseEnvelope<EmailVerifiedView>), 200)] public async Task<IActionResult> VerifyEmail(VerifyOtpRequest r) => Ok(ApiResponse.Ok(await auth.Verify(r, ChallengePurpose.VerifyEmail)));
    [HttpPost("verify-login-otp"), EnableRateLimiting("verify")][ProducesResponseType(typeof(ResponseEnvelope<LoginView>), 200)] public async Task<IActionResult> VerifyLogin(VerifyOtpRequest r) => Ok(ApiResponse.Ok(await auth.Verify(r, ChallengePurpose.Login)));
    [HttpGet("me"), Authorize][ProducesResponseType(typeof(ResponseEnvelope<UserView>), 200)] public async Task<IActionResult> Me() => Ok(ApiResponse.Ok(await auth.View((await users.FindByIdAsync(User.UserId()))!)));
}
