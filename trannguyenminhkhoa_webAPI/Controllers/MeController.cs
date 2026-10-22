using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Controllers;

[ApiController, Route("api/me"), Authorize]
public class MeController(ApplicationDbContext db, AccessService access) : ControllerBase
{
    [HttpGet("classes")][ProducesResponseType(typeof(ResponseEnvelope<PageResult<ClassView>>), 200)] public async Task<IActionResult> Classes([FromQuery] ListQuery p) { var q = access.VisibleClasses(); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.Code.Contains(p.Search) || x.Name.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.SubjectId, x.LecturerId }).PageAsync(p))); }
    [HttpGet("sessions")][ProducesResponseType(typeof(ResponseEnvelope<PageResult<SessionView>>), 200)] public async Task<IActionResult> Sessions([FromQuery] ListQuery p) { var visible = access.VisibleClasses().Select(x => x.Id); var q = db.Sessions.Where(x => visible.Contains(x.CourseClassId)); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.CourseClass.Code.Contains(p.Search) || x.CourseClass.Name.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderByDescending(x => x.StartAtUtc).Select(x => new SessionView(x.Id, x.CourseClassId, x.RoomId, x.StartAtUtc, x.EndAtUtc, x.AttendanceClosesAtUtc, x.LateAfterMinutes, x.AttendanceState)).PageAsync(p))); }
    [HttpGet("attendance"), Authorize(Roles = Roles.Student)][ProducesResponseType(typeof(ResponseEnvelope<PageResult<AttendanceView>>), 200)] public async Task<IActionResult> Attendance([FromQuery] ListQuery p) { var sid = await access.StudentId(); var q = db.Attendances.Where(x => x.StudentId == sid); if (!string.IsNullOrWhiteSpace(p.Search)) q = q.Where(x => x.ClassSession.CourseClass.Name.Contains(p.Search) || x.ClassSession.CourseClass.Code.Contains(p.Search)); return Ok(ApiResponse.Ok(await q.OrderByDescending(x => x.Id).Select(x => new AttendanceView(x.Id, x.ClassSessionId, x.StudentId, x.Status, x.CheckedInAtUtc, x.Latitude, x.Longitude, x.AccuracyMeters, x.DistanceMeters)).PageAsync(p))); }
}
[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController(ApplicationDbContext db, AccessService access) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ResponseEnvelope<DashboardView>), 200)]
    public async Task<IActionResult> Get()
    {
        var c = access.VisibleClasses(); var ids = c.Select(x => x.Id); var s = db.Sessions.Where(x => ids.Contains(x.CourseClassId)); var a = db.Attendances.Where(x => ids.Contains(x.ClassSession.CourseClassId));
        if (User.IsInRole(Roles.Student)) { var sid = await access.StudentId(); a = a.Where(x => x.StudentId == sid); }
        return Ok(ApiResponse.Ok(new { role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)!.Value, classCount = await c.CountAsync(), sessionCount = await s.CountAsync(), openSessionCount = await s.CountAsync(x => x.AttendanceState == AttendanceState.Open), presentCount = await a.CountAsync(x => x.Status == AttendanceStatus.Present), lateCount = await a.CountAsync(x => x.Status == AttendanceStatus.Late), absentCount = await a.CountAsync(x => x.Status == AttendanceStatus.Absent), excusedCount = await a.CountAsync(x => x.Status == AttendanceStatus.Excused), studentCount = User.IsInRole(Roles.Admin) ? (int?)await db.Students.CountAsync() : null, lecturerCount = User.IsInRole(Roles.Admin) ? (int?)await db.Lecturers.CountAsync() : null, userCount = User.IsInRole(Roles.Admin) ? (int?)await db.Users.CountAsync() : null }));
    }
}
