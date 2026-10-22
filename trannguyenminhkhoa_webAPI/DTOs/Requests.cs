using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using AttendanceApi.Models;
namespace AttendanceApi.DTOs;

public record RegisterRequest([Required, MaxLength(40)] string StudentCode, [Required, MaxLength(150)] string FullName, [Required, EmailAddress, MaxLength(256)] string Email, [Required, MaxLength(100)] string Username, [Required, MinLength(8), MaxLength(128)] string Password);
public record LoginRequest([Required, MaxLength(100)] string Username, [Required, MaxLength(128)] string Password);
public record VerifyOtpRequest([property: JsonRequired] Guid ChallengeId, [Required, RegularExpression(@"^\d{6}$")] string Otp);
public record ResendRequest([property: JsonRequired] Guid ChallengeId);
public record StudentRequest([Required, MaxLength(40)] string StudentCode, [Required, MaxLength(150)] string FullName, [Required, EmailAddress, MaxLength(256)] string Email);
public record LecturerRequest([Required, MaxLength(40)] string LecturerCode, [Required, MaxLength(150)] string FullName, [Required, EmailAddress, MaxLength(256)] string Email);
public record SubjectRequest([Required, MaxLength(40)] string Code, [Required, MaxLength(150)] string Name);
public record RoomRequest([Required, MaxLength(40)] string Code, [Required, MaxLength(150)] string Name, [Range(-90, 90)][property: JsonRequired] double Latitude, [Range(-180, 180)][property: JsonRequired] double Longitude, [Range(1, 5000)][property: JsonRequired] double AllowedRadiusMeters);
public record ClassRequest([Required, MaxLength(40)] string Code, [Required, MaxLength(150)] string Name, [Range(1, int.MaxValue)][property: JsonRequired] int SubjectId, [Range(1, int.MaxValue)][property: JsonRequired] int LecturerId);
public record EnrollmentRequest([Range(1, int.MaxValue)][property: JsonRequired] int StudentId);
public record SessionRequest([Range(1, int.MaxValue)][property: JsonRequired] int RoomId, [property: JsonRequired] DateTimeOffset StartAtUtc, [property: JsonRequired] DateTimeOffset EndAtUtc, [property: JsonRequired] DateTimeOffset AttendanceClosesAtUtc, [Range(0, 240)][property: JsonRequired] int LateAfterMinutes);
public record CheckInRequest([Range(1, int.MaxValue)][property: JsonRequired] int SessionId, [Required, MaxLength(256)] string QrToken, [Range(-90, 90)][property: JsonRequired] double Latitude, [Range(-180, 180)][property: JsonRequired] double Longitude, [Range(0.01, 50)][property: JsonRequired] double AccuracyMeters);
public record AttendanceUpdateRequest([property: JsonRequired] AttendanceStatus Status, [Required, MaxLength(1000)] string Reason);
public record CreateLecturerUserRequest([Range(1, int.MaxValue)][property: JsonRequired] int LecturerId, [Required, MaxLength(100)] string Username, [Required, MinLength(8), MaxLength(128)] string Password);
public record ActiveRequest([property: JsonRequired] bool IsActive);
public class ListQuery { [MaxLength(150)] public string? Search { get; set; } [Range(1, int.MaxValue)] public int Page { get; set; } = 1; [Range(1, 100)] public int PageSize { get; set; } = 20; }
public record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
public record ApiResponse(bool Success, string Message, object? Data = null, object? Errors = null) { public static ApiResponse Ok(object? data = null, string message = "Thành công.") => new(true, message, data); public static ApiResponse Fail(string message, object? errors = null, object? data = null) => new(false, message, data, errors); }
public record UserView(string Id, string Username, string FullName, string Email, string Role, int? StudentId, int? LecturerId);
public record ChallengeView(Guid ChallengeId, ChallengePurpose Purpose, string MaskedEmail, DateTimeOffset ExpiresAtUtc, DateTimeOffset ResendAvailableAtUtc);
public record LoginView(string AccessToken, DateTimeOffset ExpiresAtUtc, UserView User);
public record SessionView(int Id, int CourseClassId, int RoomId, DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc, DateTimeOffset AttendanceClosesAtUtc, int LateAfterMinutes, AttendanceState AttendanceState);
public record AttendanceView(int Id, int SessionId, int StudentId, AttendanceStatus Status, DateTimeOffset? CheckedInAtUtc, double? Latitude, double? Longitude, double? AccuracyMeters, double? DistanceMeters);
public record QrView(int SessionId, string QrPayload, DateTimeOffset ExpiresAtUtc);

// Typed response schemas shared with Swagger and API_CONTRACT.md.
public record ResponseEnvelope<T>(bool Success, string Message, T? Data, object? Errors);
public record ClassView(int Id, string Code, string Name, int SubjectId, int LecturerId);
public record EnrollmentView(int CourseClassId, int StudentId);
public record EmailVerifiedView(bool EmailVerified);
public record ActiveView(string Id, bool IsActive);
public record UserAccountView(UserView User, bool IsActive, bool EmailConfirmed);
public record ChangeView(int Id, int AttendanceId, string ChangedByUserId, DateTimeOffset ChangedAtUtc, AttendanceStatus PreviousStatus, AttendanceStatus NewStatus, string Reason);
public record DashboardView(string Role, int ClassCount, int SessionCount, int OpenSessionCount, int PresentCount, int LateCount, int AbsentCount, int ExcusedCount, int? StudentCount, int? LecturerCount, int? UserCount);
