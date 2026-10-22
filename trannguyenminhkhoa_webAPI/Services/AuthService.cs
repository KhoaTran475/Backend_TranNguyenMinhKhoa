using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace AttendanceApi.Services;

public class AuthService(ApplicationDbContext db, UserManager<ApplicationUser> users, SmtpEmailService mail, IConfiguration config)
{
    public static void IdentityCheck(IdentityResult r) { if (!r.Succeeded) throw new ApiException(400, "Thông tin tài khoản không hợp lệ.", errors: r.Errors.Select(x => new { x.Code, x.Description })); }
    public async Task<UserView> View(ApplicationUser u) => new(u.Id, u.UserName!, u.FullName, u.Email!, (await users.GetRolesAsync(u)).Single(), await db.Students.Where(x => x.UserId == u.Id).Select(x => (int?)x.Id).SingleOrDefaultAsync(), await db.Lecturers.Where(x => x.UserId == u.Id).Select(x => (int?)x.Id).SingleOrDefaultAsync());
    public async Task<ChallengeView> Register(RegisterRequest r)
    {
        ApplicationUser u;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var student = await db.Students.Where(x => x.StudentCode == r.StudentCode.Trim() && x.Email == r.Email.Trim()).Required();
            ApiSupport.Require(student.UserId == null, "Hồ sơ sinh viên đã có tài khoản.", 409);
            u = new() { UserName = r.Username.Trim(), Email = student.Email, FullName = r.FullName.Trim() };
            IdentityCheck(await users.CreateAsync(u, r.Password)); IdentityCheck(await users.AddToRoleAsync(u, Roles.Student));
            student.UserId = u.Id; await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        return await Issue(u, ChallengePurpose.VerifyEmail);
    }
    public async Task<ChallengeView> Login(LoginRequest r)
    {
        var u = await users.FindByNameAsync(r.Username.Trim());
        if (u == null || !u.IsActive || await users.IsLockedOutAsync(u)) throw new ApiException(401, "Sai thông tin đăng nhập hoặc tài khoản bị khóa.");
        if (!await users.CheckPasswordAsync(u, r.Password)) { await users.AccessFailedAsync(u); throw new ApiException(401, "Sai thông tin đăng nhập hoặc tài khoản bị khóa."); }
        await users.ResetAccessFailedCountAsync(u);
        if (!u.EmailConfirmed) { var challenge = await Issue(u, ChallengePurpose.VerifyEmail); throw new ApiException(403, "Cần xác minh email trước khi đăng nhập.", challenge); }
        return await Issue(u, ChallengePurpose.Login);
    }
    string Hash(Guid id, string code) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!), Encoding.UTF8.GetBytes($"{id:N}:{code}")));
    static ChallengeView Challenge(EmailChallenge c, string email) { var at = email.IndexOf('@'); return new(c.Id, c.Purpose, email[..1] + "***" + email[at..], c.ExpiresAtUtc, c.ResendAvailableAtUtc); }
    public async Task<ChallengeView> Issue(ApplicationUser u, ChallengePurpose purpose)
    {
        // Lock the user row so simultaneous login/resend calls cannot bypass cooldown.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [AspNetUsers] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={u.Id}");
        var now = DateTimeOffset.UtcNow;
        var old = await db.EmailChallenges.Where(x => x.UserId == u.Id && x.Purpose == purpose && !x.Consumed).ToListAsync();
        var current = old.OrderByDescending(x => x.ResendAvailableAtUtc).FirstOrDefault();
        if (current != null && current.ResendAvailableAtUtc > now) throw new ApiException(429, "Vui lòng chờ trước khi gửi lại OTP.", Challenge(current, u.Email!));
        foreach (var x in old) x.Consumed = true;
        var c = new EmailChallenge { UserId = u.Id, Purpose = purpose, ExpiresAtUtc = now.AddMinutes(5), ResendAvailableAtUtc = now.AddSeconds(60) };
        var otp = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6"); c.CodeHash = Hash(c.Id, otp); db.EmailChallenges.Add(c);
        await db.SaveChangesAsync();
        // SMTP failure keeps a recoverable challenge; no success is reported.
        string? failure = null;
        try { await mail.Send(u.Email!, otp, purpose); c.Delivered = true; }
        catch { c.ResendAvailableAtUtc = now; failure = "SMTP chưa được cấu hình hoặc gửi email thất bại. Cấu hình SMTP rồi gửi lại với challengeId này."; }
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (failure != null) throw new ApiException(503, failure, Challenge(c, u.Email!));
        return Challenge(c, u.Email!);
    }
    public async Task<ChallengeView> Resend(Guid id)
    {
        var c = await db.EmailChallenges.AsNoTracking().Where(x => x.Id == id).Required();
        ApiSupport.Require(!c.Consumed, "Challenge không còn hiệu lực.", 409);
        var u = await users.FindByIdAsync(c.UserId) ?? throw new ApiException(404, "Không tìm thấy tài khoản.");
        ApiSupport.Require(u.IsActive, "Tài khoản bị khóa.", 403);
        ApiSupport.Require(c.Purpose != ChallengePurpose.VerifyEmail || !u.EmailConfirmed, "Email đã xác minh.", 409);
        return await Issue(u, c.Purpose);
    }
    public async Task<object> Verify(VerifyOtpRequest r, ChallengePurpose purpose)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [EmailChallenges] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={r.ChallengeId}");
        var c = await db.EmailChallenges.Where(x => x.Id == r.ChallengeId).Required();
        var u = await users.FindByIdAsync(c.UserId) ?? throw new ApiException(404, "Không tìm thấy tài khoản.");
        ApiSupport.Require(c.Purpose == purpose && c.Delivered && !c.Consumed && c.FailedAttempts < 5 && c.ExpiresAtUtc > DateTimeOffset.UtcNow, "OTP hết hạn, đã sử dụng hoặc không hợp lệ.");
        ApiSupport.Require(u.IsActive, "Tài khoản bị khóa.", 403);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(c.CodeHash), Convert.FromHexString(Hash(c.Id, r.Otp))))
        {
            c.FailedAttempts++; await db.SaveChangesAsync(); await tx.CommitAsync(); throw new ApiException(400, "OTP không đúng hoặc đã đạt giới hạn 5 lần.");
        }
        c.Consumed = true;
        if (purpose == ChallengePurpose.VerifyEmail) { u.EmailConfirmed = true; IdentityCheck(await users.UpdateAsync(u)); }
        else ApiSupport.Require(u.EmailConfirmed, "Email chưa xác minh.", 403);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (purpose == ChallengePurpose.VerifyEmail) return new { emailVerified = true };
        var view = await View(u); var expires = DateTimeOffset.UtcNow.AddMinutes(60);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], config["Jwt:Audience"], new[] { new Claim(ClaimTypes.NameIdentifier, u.Id), new Claim(ClaimTypes.Name, u.UserName!), new Claim(ClaimTypes.Role, view.Role), new Claim("stamp", u.SecurityStamp!), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) }, expires: expires.UtcDateTime, signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Secret"]!)), SecurityAlgorithms.HmacSha256));
        return new LoginView(new JwtSecurityTokenHandler().WriteToken(token), expires, view);
    }
}
