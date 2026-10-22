using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/attendance"), Authorize]
public class AttendanceController(ApplicationDbContext db, AccessService access, AttendanceService service) : ControllerBase
{
    [HttpPost("check-in"), Authorize(Roles = Roles.Student)][ProducesResponseType(typeof(ResponseEnvelope<AttendanceView>), 201)] public async Task<IActionResult> CheckIn(CheckInRequest r) => StatusCode(201, ApiResponse.Ok(await service.CheckIn(r)));
    [HttpPut("{attendanceId:int}"), Authorize(Roles = "Admin,Lecturer")][ProducesResponseType(typeof(ResponseEnvelope<AttendanceView>), 200)] public async Task<IActionResult> Update(int attendanceId, AttendanceUpdateRequest r) => Ok(ApiResponse.Ok(await service.Update(attendanceId, r)));
    [HttpGet("{attendanceId:int}/changes"), Authorize(Roles = "Admin,Lecturer")][ProducesResponseType(typeof(ResponseEnvelope<PageResult<ChangeView>>), 200)] public async Task<IActionResult> Changes(int attendanceId, [FromQuery] ListQuery p) { var a = await db.Attendances.Where(x => x.Id == attendanceId).Required(); await access.Session(a.ClassSessionId); var q = db.AttendanceChanges.Where(x => x.AttendanceId == attendanceId); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.Reason.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderByDescending(x => x.Id).Select(x => new { x.Id, x.AttendanceId, x.ChangedByUserId, x.ChangedAtUtc, x.PreviousStatus, x.NewStatus, x.Reason }).PageAsync(p))); }
}
