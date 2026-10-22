using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AttendanceApi.Data;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AttendanceChecks;

public static class CheckRunner
{
    static int passed;
    static void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); passed++; }
    public static async Task<int> Main(string[] args)
    {
        Check(AttendanceRules.Distance(10.762622, 106.660172, 10.762622, 106.660172) < 0.001, "Haversine same position");
        Check(Math.Abs(AttendanceRules.Distance(0, 0, 0, 1) - 111194.93) < 1, "Haversine known equatorial distance");
        var start = DateTimeOffset.UtcNow; Check(AttendanceRules.Status(start.AddMinutes(15), start, 15) == AttendanceStatus.Present, "exact late threshold is Present"); Check(AttendanceRules.Status(start.AddMinutes(15).AddTicks(1), start, 15) == AttendanceStatus.Late, "after late threshold is Late");
        var sample = new ClassSession { StartAtUtc = start, EndAtUtc = start.AddHours(1), AttendanceClosesAtUtc = start.AddMinutes(30), AttendanceState = AttendanceState.Open };
        Check(!AttendanceRules.CanCheckIn(sample, start.AddTicks(-1)), "before start rejected"); Check(AttendanceRules.CanCheckIn(sample, start), "start included"); Check(!AttendanceRules.CanCheckIn(sample, start.AddMinutes(30)), "closing instant excluded"); sample.AttendanceState = AttendanceState.Closed; Check(!AttendanceRules.CanCheckIn(sample, start), "closed session rejected");
        if (!args.Contains("--sql")) { Console.WriteLine($"{passed} domain checks passed. Add --sql to run isolated real SQL Server + HTTP checks."); return 0; }
        var dbName = "AttendanceCheck_" + Guid.NewGuid().ToString("N");
        var csb = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ATTENDANCE_TEST_SQL") ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5") { InitialCatalog = dbName };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(csb.ConnectionString).Options;
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        var seed = new Dictionary<string, string?> { ["DemoSeed:Password"] = "TestOnly!9384xyz", ["DemoSeed:AdminEmail"] = "admin@example.invalid", ["DemoSeed:LecturerEmail"] = "lecturer@example.invalid", ["DemoSeed:Student1Email"] = "student1@example.invalid", ["DemoSeed:Student2Email"] = "student2@example.invalid", ["DemoSeed:Student3Email"] = "student3@example.invalid" };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(seed).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddDataProtection(); services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(csb.ConnectionString)); services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        using var provider = services.BuildServiceProvider(); Process? api = null;
        var output = new List<string>();
        try
        {
            await using (var db = new ApplicationDbContext(options)) await db.Database.MigrateAsync();
            using (var scope = provider.CreateScope()) await DemoSeeder.SeedAdmin(scope.ServiceProvider, cfg);
            using (var scope = provider.CreateScope()) await DemoSeeder.SeedAdmin(scope.ServiceProvider, cfg);
            await using (var db = new ApplicationDbContext(options))
                Check(await db.Users.CountAsync() == 1 && await db.Roles.CountAsync() == 3, "Admin-only seed is idempotent and initializes all roles");
            using (var scope = provider.CreateScope()) await DemoSeeder.Seed(scope.ServiceProvider, cfg);
            using (var scope = provider.CreateScope()) await DemoSeeder.Seed(scope.ServiceProvider, cfg);
            await using (var db = new ApplicationDbContext(options)) { Check(await db.Users.CountAsync() == 5 && await db.Sessions.CountAsync() == 1, "SQL migrations + idempotent demo seed"); Check(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()).All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "all foreign keys restrict deletion"); }
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")); var project = Path.Combine(root, "trannguyenminhkhoa_webAPI");
            var psi = new ProcessStartInfo("dotnet", $"\"{Path.Combine(project, "bin/Debug/net8.0/trannguyenminhkhoa_webAPI.dll")}\"") { WorkingDirectory = project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var kv in new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development", ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}", ["ConnectionStrings__DefaultConnection"] = csb.ConnectionString, ["Jwt__Secret"] = secret, ["Jwt__Issuer"] = "AttendanceApi", ["Jwt__Audience"] = "AttendanceFlutter", ["Smtp__Host"] = "", ["Smtp__Sender"] = "" }) psi.Environment[kv.Key] = kv.Value;
            api = Process.Start(psi)!; api.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.Add(e.Data); }; api.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.Add(e.Data); }; api.BeginOutputReadLine(); api.BeginErrorReadLine();
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(30) };
            for (int i = 0; i < 60; i++) { try { if ((await http.GetAsync("/swagger/v1/swagger.json")).IsSuccessStatusCode) break; } catch (HttpRequestException) { } await Task.Delay(250); }
            async Task<(int Status, JsonObject Body)> Call(string method, string path, object? data = null, string? token = null)
            {
                using var req = new HttpRequestMessage(new HttpMethod(method), path); if (data != null) req.Content = JsonContent.Create(data); if (token != null) req.Headers.Authorization = new("Bearer", token); using var res = await http.SendAsync(req); var raw = await res.Content.ReadAsStringAsync(); return ((int)res.StatusCode, JsonNode.Parse(raw)!.AsObject());
            }
            const string otp = "938461";
            async Task<Guid> TestChallenge(string username, ChallengePurpose purpose = ChallengePurpose.Login, int failures = 0, bool expired = false)
            {
                await using var db = new ApplicationDbContext(options); var u = await db.Users.SingleAsync(x => x.UserName == username); var c = new EmailChallenge { UserId = u.Id, Purpose = purpose, Delivered = true, FailedAttempts = failures, ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(expired ? -1 : 5), ResendAvailableAtUtc = DateTimeOffset.UtcNow.AddSeconds(60) }; c.CodeHash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{c.Id:N}:{otp}"))); db.EmailChallenges.Add(c); await db.SaveChangesAsync(); return c.Id;
            }
            async Task<string> Token(string username) { var id = await TestChallenge(username); var r = await Call("POST", "/api/auth/verify-login-otp", new { challengeId = id, otp }); Check(r.Status == 200, $"OTP -> JWT: {username}"); return r.Body["data"]!["accessToken"]!.GetValue<string>(); }
            var missing = await Call("GET", "/api/missing"); Check(missing.Status == 404 && missing.Body["success"]!.GetValue<bool>() == false, "404 envelope");
            Check((await Call("GET", "/api/auth/me")).Status == 401, "401 envelope");
            var smtp = await Call("POST", "/api/auth/login", new { username = "student1.demo", password = "TestOnly!9384xyz" }); Check(smtp.Status == 503 && smtp.Body["data"]?["challengeId"] != null && smtp.Body["data"]?["accessToken"] == null, "SMTP failure returns recoverable challenge and no JWT");
            Check(smtp.Body["data"]!["purpose"]!.GetValue<string>() == "Login", "error middleware preserves string enum contract");
            var old = smtp.Body["data"]!["challengeId"]!.GetValue<Guid>(); var resend = await Call("POST", "/api/auth/resend-otp", new { challengeId = old }); Check(resend.Status == 503 && resend.Body["data"]!["challengeId"]!.GetValue<Guid>() != old, "SMTP failure permits immediate resend and replaces code");
            var admin = await Token("admin.demo"); var lecturer = await Token("lecturer.demo"); var student = await Token("student1.demo");
            var used = await TestChallenge("student2.demo"); Check((await Call("POST", "/api/auth/verify-email", new { challengeId = used, otp })).Status == 400, "OTP purpose cannot be swapped"); Check((await Call("POST", "/api/auth/verify-login-otp", new { challengeId = used, otp })).Status == 200, "correct OTP succeeds"); Check((await Call("POST", "/api/auth/verify-login-otp", new { challengeId = used, otp })).Status == 400, "OTP cannot be replayed");
            var wrong = await TestChallenge("student3.demo"); for (int i = 0; i < 5; i++) Check((await Call("POST", "/api/auth/verify-login-otp", new { challengeId = wrong, otp = "000000" })).Status == 400, $"wrong OTP attempt {i + 1}"); Check((await Call("POST", "/api/auth/verify-login-otp", new { challengeId = wrong, otp })).Status == 400, "correct OTP rejected after five failures");
            var exp = await TestChallenge("student3.demo", expired: true); Check((await Call("POST", "/api/auth/verify-login-otp", new { challengeId = exp, otp })).Status == 400, "expired OTP rejected");
            var cd = await TestChallenge("student3.demo"); Check((await Call("POST", "/api/auth/resend-otp", new { challengeId = cd })).Status == 429, "60 second resend cooldown");
            Check((await Call("GET", "/api/students", token: student)).Status == 403, "Student cannot read admin student directory");
            var paged = await Call("GET", "/api/students?search=SV00&page=1&pageSize=2", token: admin); Check(paged.Status == 200 && paged.Body["data"]!["items"]!.AsArray().Count == 2 && paged.Body["data"]!["totalCount"]!.GetValue<int>() == 3, "search + pagination"); Check((await Call("GET", "/api/students?pageSize=101", token: admin)).Status == 400, "query validation envelope"); Check((await Call("PATCH", "/api/users/missing", new { }, admin)).Status == 404, "unknown route remains 404"); Check((await Call("PATCH", "/api/users/missing/active", new { }, admin)).Status == 400, "missing isActive is rejected");
            int session, cl, room, sid; string studentUser;
            await using (var db = new ApplicationDbContext(options)) { session = await db.Sessions.Select(x => x.Id).SingleAsync(); cl = await db.Classes.Select(x => x.Id).SingleAsync(); room = await db.Rooms.Select(x => x.Id).SingleAsync(); var st = await db.Students.SingleAsync(x => x.StudentCode == "SV001"); sid = st.Id; studentUser = st.UserId!; }
            // Registration rollback and recoverability use actual SQL; email delivery is intentionally unavailable.
            Check((await Call("POST", "/api/auth/register", new { studentCode = "NO-SUCH-STUDENT", fullName = "Test", email = "new@example.invalid", username = "new.test", password = "TestOnly!9384xyz" })).Status == 404, "registration needs pre-created profile");
            var profile = await Call("POST", "/api/students", new { studentCode = "SVNEW", fullName = "New Test", email = "new@example.invalid" }, admin); Check(profile.Status == 201, "Admin creates Student profile");
            var reg = await Call("POST", "/api/auth/register", new { studentCode = "SVNEW", fullName = "New Test", email = "new@example.invalid", username = "new.test", password = "TestOnly!9384xyz", role = "Admin" }); Check(reg.Status == 503, "registration SMTP failure is recoverable");
            await using (var db = new ApplicationDbContext(options)) { var u = await db.Users.SingleAsync(x => x.UserName == "new.test"); Check(await db.Students.AnyAsync(x => x.UserId == u.Id), "registration links profile transactionally"); var role = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ur.UserId == u.Id select r.Name).SingleAsync(); Check(role == Roles.Student, "self-registration cannot select Admin role"); }
            var verify = await TestChallenge("new.test", ChallengePurpose.VerifyEmail); Check((await Call("POST", "/api/auth/verify-email", new { challengeId = verify, otp })).Status == 200, "email verification persists"); var outsider = await Token("new.test");
            Check((await Call("POST", $"/api/sessions/{session}/open", token: student)).Status == 403, "Student cannot open session"); Check((await Call("POST", $"/api/sessions/{session}/open", token: lecturer)).Status == 200, "assigned Lecturer opens session");
            var qr1 = await Call("GET", $"/api/sessions/{session}/qr", token: lecturer); var qr2 = await Call("GET", $"/api/sessions/{session}/qr", token: lecturer); Check(qr1.Status == 200 && qr1.Body["data"]!["qrPayload"]!.GetValue<string>() == qr2.Body["data"]!["qrPayload"]!.GetValue<string>(), "QR remains stable while valid"); var qr = JsonNode.Parse(qr1.Body["data"]!["qrPayload"]!.GetValue<string>())!["token"]!.GetValue<string>();
            object CheckBody(string token, double lat = 10.762622, double accuracy = 10) => new { sessionId = session, qrToken = token, latitude = lat, longitude = 106.660172, accuracyMeters = accuracy };
            Check((await Call("POST", "/api/attendance/check-in", CheckBody(qr), outsider)).Status == 403, "non-enrolled Student denied"); Check((await Call("POST", "/api/attendance/check-in", CheckBody("wrong"), student)).Status == 400, "wrong QR denied"); Check((await Call("POST", "/api/attendance/check-in", CheckBody(qr, 0), student)).Status == 400, "outside radius denied"); Check((await Call("POST", "/api/attendance/check-in", CheckBody(qr, accuracy: 51), student)).Status == 400, "poor GPS accuracy denied");
            var duo = await Task.WhenAll(Call("POST", "/api/attendance/check-in", CheckBody(qr), student), Call("POST", "/api/attendance/check-in", CheckBody(qr), student)); Check(duo.Count(x => x.Status == 201) == 1 && duo.Count(x => x.Status == 409) == 1, "concurrent check-in creates exactly one result"); var attendance = duo.Single(x => x.Status == 201).Body["data"]!["id"]!.GetValue<int>(); Check(duo.Single(x => x.Status == 201).Body["data"]!["status"]!.GetValue<string>() == "Present", "on-time check-in is Present");
            await using (var db = new ApplicationDbContext(options)) { db.Attendances.Add(new() { ClassSessionId = session, StudentId = sid, Status = AttendanceStatus.Present }); try { await db.SaveChangesAsync(); Check(false, "SQL unique attendance constraint"); } catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { Check(true, "SQL unique attendance constraint"); } }
            var student2 = await Token("student2.demo"); Check((await Call("POST", "/api/attendance/check-in", CheckBody(qr), student2)).Status == 201, "same QR accepts a second enrolled Student");
            Check((await Call("POST", $"/api/classes/{cl}/students", new { studentId = profile.Body["data"]!["id"]!.GetValue<int>() }, admin)).Status == 409, "roster is frozen after opening");
            Check((await Call("PUT", $"/api/attendance/{attendance}", new { status = "Excused", reason = "" }, lecturer)).Status == 400, "manual adjustment requires reason"); Check((await Call("PUT", $"/api/attendance/{attendance}", new { status = "Excused", reason = "Test correction" }, lecturer)).Status == 200, "Lecturer adjusts result"); var logs = await Call("GET", $"/api/attendance/{attendance}/changes", token: lecturer); Check(logs.Body["data"]!["items"]!.AsArray().Count == 1 && logs.Body["data"]!["items"]![0]!["previousStatus"]!.GetValue<string>() == "Present", "adjustment audit preserves old/new status");
            Check((await Call("POST", $"/api/sessions/{session}/close", token: lecturer)).Status == 200, "Lecturer closes session"); await using (var db = new ApplicationDbContext(options)) Check(await db.Attendances.CountAsync(x => x.ClassSessionId == session) == 3 && await db.Attendances.CountAsync(x => x.ClassSessionId == session && x.Status == AttendanceStatus.Absent) == 1, "close fills missing students as Absent");
            Check((await Call("POST", $"/api/sessions/{session}/open", token: lecturer)).Status == 409, "cannot reopen closed session"); Check((await Call("POST", "/api/attendance/check-in", CheckBody(qr), student)).Status == 409, "closed session invalidates QR"); Check((await Call("DELETE", $"/api/students/{sid}", token: admin)).Status == 409, "used student cannot be deleted");
            var now = DateTimeOffset.UtcNow; var late = await Call("POST", $"/api/classes/{cl}/sessions", new { roomId = room, startAtUtc = now.AddMinutes(-30), endAtUtc = now.AddHours(1), attendanceClosesAtUtc = now.AddMinutes(30), lateAfterMinutes = 10 }, lecturer); var lateId = late.Body["data"]!["id"]!.GetValue<int>(); await Call("POST", $"/api/sessions/{lateId}/open", token: lecturer); var lqr = await Call("GET", $"/api/sessions/{lateId}/qr", token: lecturer); var lt = JsonNode.Parse(lqr.Body["data"]!["qrPayload"]!.GetValue<string>())!["token"]!.GetValue<string>(); var lateResult = await Call("POST", "/api/attendance/check-in", new { sessionId = lateId, qrToken = lt, latitude = 10.762622, longitude = 106.660172, accuracyMeters = 10 }, student); Check(lateResult.Status == 201 && lateResult.Body["data"]!["status"]!.GetValue<string>() == "Late", "late check-in uses server time");
            // A valid lecturer linked to another profile must not manage this class.
            var other = await Call("POST", "/api/lecturers", new { lecturerCode = "OTHER", fullName = "Other", email = "other@example.invalid" }, admin); var otherId = other.Body["data"]!["id"]!.GetValue<int>(); Check((await Call("POST", "/api/users/lecturer", new { lecturerId = otherId, username = "other.lecturer", password = "TestOnly!9384xyz" }, admin)).Status == 201, "Admin creates linked Lecturer account");
            await using (var db = new ApplicationDbContext(options)) { var u = await db.Users.SingleAsync(x => x.UserName == "other.lecturer"); u.EmailConfirmed = true; await db.SaveChangesAsync(); }
            var otherToken = await Token("other.lecturer"); Check((await Call("GET", $"/api/sessions/{session}/attendance", token: otherToken)).Status == 403, "Lecturer cannot access another Lecturer's class");
            Check((await Call("PATCH", $"/api/users/{studentUser}/active", new { isActive = false }, admin)).Status == 200, "Admin disables account"); Check((await Call("GET", "/api/auth/me", token: student)).Status == 401, "disabling invalidates existing JWT immediately"); await Call("PATCH", $"/api/users/{studentUser}/active", new { isActive = true }, admin); Check((await Call("GET", "/api/auth/me", token: student)).Status == 401, "reactivation does not revive old JWT");
            var swagger = JsonNode.Parse(await http.GetStringAsync("/swagger/v1/swagger.json"))!; Check(swagger["paths"]!.AsObject().Count >= 30, "Swagger includes business endpoints");
            Check(swagger["paths"]!["/api/auth/login"]!["post"]!["security"] is not JsonArray secured || secured.Count == 0, "Swagger login does not require JWT");
            Check(swagger["paths"]!["/api/attendance/check-in"]!["post"]!["security"]!.AsArray().Count == 1, "Swagger protected endpoints require Bearer");
            var schemas = swagger["components"]!["schemas"]!.AsObject(); Check(schemas["AttendanceStatus"]!["type"]!.GetValue<string>() == "string", "Swagger enums match string JSON contract");
            Check(schemas["Student"]!["properties"]!.AsObject().ContainsKey("userId") && !schemas["Student"]!["properties"]!.AsObject().ContainsKey("user"), "Swagger excludes navigation/security objects");
            var successSchema = swagger["paths"]!["/api/attendance/check-in"]!["post"]!["responses"]!["201"]!["content"]!["application/json"]!["schema"]!["$ref"]!.GetValue<string>().Split('/').Last(); Check(schemas[successSchema]!["properties"]!.AsObject().ContainsKey("data"), "Swagger success describes response envelope");

            Console.WriteLine($"{passed} checks passed against real SQL Server and HTTP. SMTP delivery/Conveyor were NOT tested. OTP fixtures were inserted only into this isolated test database."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); lock (output) foreach (var line in output.TakeLast(12)) Console.Error.WriteLine(line); return 1; }
        finally
        {
            if (api is { HasExited: false }) { api.Kill(true); await api.WaitForExitAsync(); }
            api?.Dispose();
            SqlConnection.ClearAllPools();
            // This name is generated here and never accepted from user configuration.
            if (dbName.StartsWith("AttendanceCheck_", StringComparison.Ordinal) && Guid.TryParseExact(dbName[16..], "N", out _)) { await using var db = new ApplicationDbContext(options); await db.Database.EnsureDeletedAsync(); Console.WriteLine("Removed isolated test database: " + dbName); }
        }
    }
}
