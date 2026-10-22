using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Data;

public static class DemoSeeder
{
    public static async Task SeedAdmin(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Chạy Update-Database trước khi tạo Admin.");
        var email = config["DemoSeed:AdminEmail"]?.Trim();
        var password = config["DemoSeed:Password"];
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Cần cấu hình DemoSeed:AdminEmail và DemoSeed:Password.");

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        foreach (var role in new[] { Roles.Admin, Roles.Lecturer, Roles.Student })
            if (!await roles.RoleExistsAsync(role))
                AuthService.IdentityCheck(await roles.CreateAsync(new IdentityRole(role)));

        var existing = await users.FindByNameAsync("admin.demo");
        if (existing != null)
        {
            if (!string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase) ||
                !await users.IsInRoleAsync(existing, Roles.Admin))
                throw new InvalidOperationException("admin.demo đã tồn tại với cấu hình khác; không ghi đè.");
        }
        else
        {
            var admin = new ApplicationUser
            {
                UserName = "admin.demo", Email = email, FullName = "Quản trị demo",
                EmailConfirmed = true, IsActive = true
            };
            AuthService.IdentityCheck(await users.CreateAsync(admin, password));
            AuthService.IdentityCheck(await users.AddToRoleAsync(admin, Roles.Admin));
        }
        await tx.CommitAsync();
    }

    public static async Task Seed(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<ApplicationDbContext>(); var users = services.GetRequiredService<UserManager<ApplicationUser>>(); var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any()) throw new InvalidOperationException("Chạy Update-Database đúng database demo trước khi seed.");
        var c = config.GetSection("DemoSeed"); var password = c["Password"];
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Thiếu DemoSeed:Password.");
        var keys = new[] { "AdminEmail", "LecturerEmail", "Student1Email", "Student2Email", "Student3Email" };
        foreach (var key in keys) if (!System.Net.Mail.MailAddress.TryCreate(c[key], out _)) throw new InvalidOperationException($"Thiếu email hợp lệ: DemoSeed:{key}");
        if (keys.Select(k => c[k]!.ToUpperInvariant()).Distinct().Count() != 5) throw new InvalidOperationException("Năm email demo phải khác nhau; dùng địa chỉ/alias do bạn quản lý.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        foreach (var r in new[] { Roles.Admin, Roles.Lecturer, Roles.Student }) if (!await roles.RoleExistsAsync(r)) AuthService.IdentityCheck(await roles.CreateAsync(new(r)));
        async Task<ApplicationUser> User(string username, string email, string name, string role)
        {
            var u = await users.FindByNameAsync(username);
            if (u != null) { if (u.Email != email || !(await users.IsInRoleAsync(u, role))) throw new InvalidOperationException($"Tài khoản {username} đã tồn tại với cấu hình khác. Không ghi đè."); return u; }
            u = new() { UserName = username, Email = email, FullName = name, EmailConfirmed = true }; AuthService.IdentityCheck(await users.CreateAsync(u, password)); AuthService.IdentityCheck(await users.AddToRoleAsync(u, role)); return u;
        }
        await User("admin.demo", c["AdminEmail"]!, "Quản trị demo", Roles.Admin);
        var lu = await User("lecturer.demo", c["LecturerEmail"]!, "Giảng viên demo", Roles.Lecturer);
        var lecturer = await db.Lecturers.SingleOrDefaultAsync(x => x.LecturerCode == "GV001");
        if (lecturer == null) { lecturer = new() { LecturerCode = "GV001", FullName = lu.FullName, Email = lu.Email!, UserId = lu.Id }; db.Lecturers.Add(lecturer); }
        else if (lecturer.UserId != lu.Id) throw new InvalidOperationException("GV001 đã tồn tại; không chiếm hồ sơ.");
        var students = new List<Student>();
        for (var i = 1; i <= 3; i++) { var u = await User($"student{i}.demo", c[$"Student{i}Email"]!, $"Sinh viên demo {i}", Roles.Student); var code = $"SV00{i}"; var st = await db.Students.SingleOrDefaultAsync(x => x.StudentCode == code); if (st == null) { st = new() { StudentCode = code, FullName = u.FullName, Email = u.Email!, UserId = u.Id }; db.Students.Add(st); } else if (st.UserId != u.Id) throw new InvalidOperationException($"{code} đã tồn tại; không chiếm hồ sơ."); students.Add(st); }
        var subject = await db.Subjects.SingleOrDefaultAsync(x => x.Code == "LTDD"); if (subject == null) { subject = new() { Code = "LTDD", Name = "Lập trình di động" }; db.Subjects.Add(subject); }
        if (!await db.Subjects.AnyAsync(x => x.Code == "WEBAPI")) db.Subjects.Add(new() { Code = "WEBAPI", Name = "Lập trình Web API" });
        var room = await db.Rooms.SingleOrDefaultAsync(x => x.Code == "DEMO101"); if (room == null) { room = new() { Code = "DEMO101", Name = "Phòng demo", Latitude = 10.762622, Longitude = 106.660172, AllowedRadiusMeters = 100 }; db.Rooms.Add(room); }
        await db.SaveChangesAsync();
        var cl = await db.Classes.SingleOrDefaultAsync(x => x.Code == "LTDD-DEMO");
        if (cl == null)
        {
            cl = new() { Code = "LTDD-DEMO", Name = "Lớp LTDD demo", LecturerId = lecturer.Id, SubjectId = subject.Id }; db.Classes.Add(cl); await db.SaveChangesAsync();
            foreach (var s in students) db.Enrollments.Add(new() { CourseClassId = cl.Id, StudentId = s.Id });
            var now = DateTimeOffset.UtcNow; db.Sessions.Add(new() { CourseClassId = cl.Id, RoomId = room.Id, StartAtUtc = now.AddMinutes(-1), EndAtUtc = now.AddHours(4), AttendanceClosesAtUtc = now.AddHours(3), LateAfterMinutes = 15 });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
