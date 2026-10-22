using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

using Microsoft.AspNetCore.Identity;
[ApiController, Route("api/users"), Authorize(Roles = Roles.Admin)]
public class UsersController(ApplicationDbContext db, UserManager<ApplicationUser> users, AuthService auth) : ControllerBase
{
    [HttpGet][ProducesResponseType(typeof(ResponseEnvelope<PageResult<UserAccountView>>), 200)] public async Task<IActionResult> List([FromQuery] ListQuery p) { var q = db.Users.AsNoTracking(); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.UserName!.Contains(p.Search) || x.FullName.Contains(p.Search) || x.Email!.Contains(p.Search)); var page = await q.OrderBy(x => x.UserName).PageAsync(p); var items = new List<object>(); foreach (var u in page.Items) items.Add(new { user = await auth.View(u), u.IsActive, u.EmailConfirmed }); return Ok(ApiResponse.Ok(new PageResult<object>(items, page.TotalCount, p.Page, p.PageSize))); }
    [HttpPost("lecturer")]
    [ProducesResponseType(typeof(ResponseEnvelope<UserView>), 201)]
    public async Task<IActionResult> Create(CreateLecturerUserRequest r)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var l = await db.Lecturers.Where(x => x.Id == r.LecturerId).Required(); ApiSupport.Require(l.UserId == null, "Giảng viên đã liên kết tài khoản.", 409);
        var u = new ApplicationUser { UserName = r.Username.Trim(), Email = l.Email, FullName = l.FullName, EmailConfirmed = false }; AuthService.IdentityCheck(await users.CreateAsync(u, r.Password)); AuthService.IdentityCheck(await users.AddToRoleAsync(u, Roles.Lecturer)); l.UserId = u.Id; await db.SaveChangesAsync(); await tx.CommitAsync();
        // First password login sends an email-verification challenge; no token is issued.
        return StatusCode(201, ApiResponse.Ok(await auth.View(u), "Đã tạo tài khoản. Đăng nhập để nhận OTP xác minh email."));
    }
    [HttpPatch("{userId}/active")][ProducesResponseType(typeof(ResponseEnvelope<ActiveView>), 200)] public async Task<IActionResult> Active(string userId, ActiveRequest r) { ApiSupport.Require(userId != User.UserId(), "Không tự khóa/mở khóa tài khoản đang sử dụng.", 409); var u = await users.FindByIdAsync(userId) ?? throw new ApiException(404, "Không tìm thấy tài khoản."); u.IsActive = r.IsActive; AuthService.IdentityCheck(await users.UpdateAsync(u)); AuthService.IdentityCheck(await users.UpdateSecurityStampAsync(u)); return Ok(ApiResponse.Ok(new { u.Id, u.IsActive })); }
}
