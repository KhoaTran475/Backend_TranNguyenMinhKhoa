using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
namespace AttendanceApi.Services;

public class ApiException(int status, string message, object? data = null, object? errors = null) : Exception(message) { public int Status { get; } = status; public object? DataValue { get; } = data; public object? Errors { get; } = errors; }
public static class ApiSupport
{
    public static string UserId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ApiException(401, "Cần đăng nhập.");
    public static async Task<PageResult<T>> PageAsync<T>(this IQueryable<T> q, ListQuery p) { var total = await q.CountAsync(); return new(await q.Skip((int)Math.Min((long)(p.Page - 1) * p.PageSize, int.MaxValue)).Take(p.PageSize).ToListAsync(), total, p.Page, p.PageSize); }
    public static SessionView View(this ClassSession x) => new(x.Id, x.CourseClassId, x.RoomId, x.StartAtUtc, x.EndAtUtc, x.AttendanceClosesAtUtc, x.LateAfterMinutes, x.AttendanceState);
    public static AttendanceView View(this Models.Attendance x) => new(x.Id, x.ClassSessionId, x.StudentId, x.Status, x.CheckedInAtUtc, x.Latitude, x.Longitude, x.AccuracyMeters, x.DistanceMeters);
    public static void Require(bool condition, string message, int status = 400) { if (!condition) throw new ApiException(status, message); }
    public static async Task<T> Required<T>(this IQueryable<T> q) where T : class => await q.FirstOrDefaultAsync() ?? throw new ApiException(404, "Không tìm thấy dữ liệu.");
}
public class AccessService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    public ClaimsPrincipal User => accessor.HttpContext!.User;
    public async Task<int> StudentId()
    {
        var id = await db.Students.Where(x => x.UserId == User.UserId()).Select(x => x.Id).FirstOrDefaultAsync();
        if (id == 0) throw new ApiException(403, "Tài khoản chưa liên kết sinh viên.");
        return id;
    }
    public async Task<CourseClass> Class(int id, bool allowStudent = false)
    {
        var c = await db.Classes.Include(x => x.Lecturer).Where(x => x.Id == id).Required();
        if (User.IsInRole(Roles.Admin)) return c;
        if (User.IsInRole(Roles.Lecturer) && c.Lecturer.UserId == User.UserId()) return c;
        if (allowStudent && User.IsInRole(Roles.Student) && await db.Enrollments.AnyAsync(x => x.CourseClassId == id && x.Student.UserId == User.UserId())) return c;
        throw new ApiException(403, "Bạn không có quyền truy cập lớp này.");
    }
    public async Task<ClassSession> Session(int id, bool allowStudent = false) { var s = await db.Sessions.Where(x => x.Id == id).Required(); await Class(s.CourseClassId, allowStudent); return s; }
    public IQueryable<CourseClass> VisibleClasses() { var uid = User.UserId(); return User.IsInRole(Roles.Admin) ? db.Classes : User.IsInRole(Roles.Lecturer) ? db.Classes.Where(x => x.Lecturer.UserId == uid) : db.Classes.Where(x => db.Enrollments.Any(e => e.CourseClassId == x.Id && e.Student.UserId == uid)); }
}
