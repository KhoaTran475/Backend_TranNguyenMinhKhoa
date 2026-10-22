using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Services;

public static class AttendanceRules
{
    public static double Distance(double lat1, double lon1, double lat2, double lon2) { const double r = 6371000; var a1 = lat1 * Math.PI / 180; var a2 = lat2 * Math.PI / 180; var dlat = (lat2 - lat1) * Math.PI / 180; var dlon = (lon2 - lon1) * Math.PI / 180; var a = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(a1) * Math.Cos(a2) * Math.Pow(Math.Sin(dlon / 2), 2); return r * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1))); }
    public static AttendanceStatus Status(DateTimeOffset now, DateTimeOffset start, int lateMinutes) => now <= start.AddMinutes(lateMinutes) ? AttendanceStatus.Present : AttendanceStatus.Late;
    public static bool CanCheckIn(ClassSession s, DateTimeOffset now) => s.AttendanceState == AttendanceState.Open && now >= s.StartAtUtc && now < s.AttendanceClosesAtUtc && now < s.EndAtUtc;
}
public class AttendanceService(ApplicationDbContext db, AccessService access, IDataProtectionProvider protection)
{
    readonly IDataProtector protector = protection.CreateProtector("attendance-qr-v1");
    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    // All state transitions and check-ins lock the same session row until commit.
    public async Task<ClassSession> Lock(int id, bool allowStudent = false)
    {
        var classId = await db.Sessions.Where(x => x.Id == id).Select(x => (int?)x.CourseClassId).SingleOrDefaultAsync() ?? throw new ApiException(404, "Không tìm thấy buổi học.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Classes] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={classId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Sessions] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}");
        return await access.Session(id, allowStudent);
    }
    public async Task CloseCore(ClassSession s)
    {
        ApiSupport.Require(s.AttendanceState == AttendanceState.Open, "Chỉ đóng phiên đang mở.", 409);
        var missing = await db.Enrollments.Where(x => x.CourseClassId == s.CourseClassId && !db.Attendances.Any(a => a.ClassSessionId == s.Id && a.StudentId == x.StudentId)).Select(x => x.StudentId).ToListAsync();
        foreach (var id in missing) db.Attendances.Add(new() { ClassSessionId = s.Id, StudentId = id, Status = AttendanceStatus.Absent });
        s.AttendanceState = AttendanceState.Closed; s.QrTokenHash = null; s.QrTokenProtected = null; s.QrExpiresAtUtc = null; await db.SaveChangesAsync();
    }
    public async Task<SessionView> Open(int id) { await using var tx = await db.Database.BeginTransactionAsync(); var s = await Lock(id); var now = DateTimeOffset.UtcNow; ApiSupport.Require(s.AttendanceState == AttendanceState.Scheduled, "Phiên đã mở hoặc đã đóng; không hỗ trợ mở lại.", 409); ApiSupport.Require(now >= s.StartAtUtc && now < s.AttendanceClosesAtUtc, "Chưa đến giờ hoặc đã quá thời gian mở phiên.", 409); s.AttendanceState = AttendanceState.Open; await db.SaveChangesAsync(); await tx.CommitAsync(); return s.View(); }
    public async Task<SessionView> Close(int id) { await using var tx = await db.Database.BeginTransactionAsync(); var s = await Lock(id); await CloseCore(s); await tx.CommitAsync(); return s.View(); }
    public async Task<QrView> Qr(int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var s = await Lock(id); var now = DateTimeOffset.UtcNow; ApiSupport.Require(AttendanceRules.CanCheckIn(s, now), "Phiên chưa mở hoặc đã hết giờ.", 409);
        string? token = null;
        if (s.QrExpiresAtUtc > now && s.QrTokenProtected != null) { try { token = protector.Unprotect(s.QrTokenProtected); } catch (CryptographicException) { throw new ApiException(503, "Không đọc được QR hiện hành. Giữ khóa Data Protection hoặc chờ QR hết hạn."); } }
        if (token == null) { token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); s.QrTokenHash = Hash(token); s.QrTokenProtected = protector.Protect(token); s.QrExpiresAtUtc = new[] { now.AddMinutes(2), s.AttendanceClosesAtUtc, s.EndAtUtc }.Min(); await db.SaveChangesAsync(); }
        await tx.CommitAsync(); return new(s.Id, JsonSerializer.Serialize(new { type = "attendance", sessionId = s.Id, token }), s.QrExpiresAtUtc!.Value);
    }
    public async Task<AttendanceView> CheckIn(CheckInRequest r)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var s = await Lock(r.SessionId, true); var sid = await access.StudentId(); var now = DateTimeOffset.UtcNow;
        ApiSupport.Require(await db.Enrollments.AnyAsync(x => x.CourseClassId == s.CourseClassId && x.StudentId == sid), "Sinh viên không thuộc lớp.", 403);
        ApiSupport.Require(AttendanceRules.CanCheckIn(s, now), "Phiên chưa mở hoặc đã hết giờ.", 409);
        ApiSupport.Require(s.QrExpiresAtUtc > now && s.QrTokenHash != null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(s.QrTokenHash), Convert.FromHexString(Hash(r.QrToken))), "QR sai hoặc hết hạn.");
        ApiSupport.Require(double.IsFinite(r.Latitude) && double.IsFinite(r.Longitude) && double.IsFinite(r.AccuracyMeters) && r.AccuracyMeters > 0 && r.AccuracyMeters <= 50, "Vị trí không hợp lệ hoặc độ chính xác quá thấp. Lấy lại vị trí.");
        var room = await db.Rooms.Where(x => x.Id == s.RoomId).Required(); var distance = AttendanceRules.Distance(r.Latitude, r.Longitude, room.Latitude, room.Longitude);
        ApiSupport.Require(distance <= room.AllowedRadiusMeters, "Bạn đang ở ngoài bán kính điểm danh.");
        ApiSupport.Require(!await db.Attendances.AnyAsync(x => x.ClassSessionId == s.Id && x.StudentId == sid), "Bạn đã có kết quả điểm danh.", 409);
        var a = new Models.Attendance { ClassSessionId = s.Id, StudentId = sid, CheckedInAtUtc = now, Status = AttendanceRules.Status(now, s.StartAtUtc, s.LateAfterMinutes), Latitude = r.Latitude, Longitude = r.Longitude, AccuracyMeters = r.AccuracyMeters, DistanceMeters = distance }; db.Attendances.Add(a); await db.SaveChangesAsync(); await tx.CommitAsync(); return a.View();
    }
    public async Task<AttendanceView> Update(int id, AttendanceUpdateRequest r)
    {
        ApiSupport.Require(Enum.IsDefined(r.Status) && !string.IsNullOrWhiteSpace(r.Reason), "Trạng thái hoặc lý do không hợp lệ.");
        var sessionId = await db.Attendances.Where(x => x.Id == id).Select(x => (int?)x.ClassSessionId).SingleOrDefaultAsync() ?? throw new ApiException(404, "Không tìm thấy điểm danh.");
        await using var tx = await db.Database.BeginTransactionAsync(); var s = await Lock(sessionId); ApiSupport.Require(s.AttendanceState != AttendanceState.Scheduled, "Phiên chưa bắt đầu.", 409);
        var a = await db.Attendances.Where(x => x.Id == id).Required(); db.AttendanceChanges.Add(new() { AttendanceId = id, ChangedByUserId = access.User.UserId(), ChangedAtUtc = DateTimeOffset.UtcNow, PreviousStatus = a.Status, NewStatus = r.Status, Reason = r.Reason.Trim() }); a.Status = r.Status; await db.SaveChangesAsync(); await tx.CommitAsync(); return a.View();
    }
}
