using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AttendanceApi.Data;
using AttendanceApi.DTOs;
using AttendanceApi.Models;
using AttendanceApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["Jwt:Secret"];
// EF tools use the design-time factory; runtime refuses missing secrets.
if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32 || secret.Contains("CHANGE_ME")) throw new InvalidOperationException("Cấu hình Jwt:Secret ngẫu nhiên tối thiểu 32 byte bằng User Secrets hoặc biến môi trường.");
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentityCore<ApplicationUser>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 8; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5); }).AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new() { ValidateIssuer = true, ValidateAudience = true, ValidateIssuerSigningKey = true, ValidateLifetime = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"], IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ClockSkew = TimeSpan.Zero };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async c => { var users = c.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>(); var u = await users.FindByIdAsync(c.Principal!.UserId()); if (u == null || !u.IsActive || !u.EmailConfirmed || c.Principal!.FindFirst("stamp")?.Value != u.SecurityStamp) c.Fail("Tài khoản bị khóa hoặc phiên không hợp lệ."); },
        OnChallenge = async c => { c.HandleResponse(); c.Response.StatusCode = 401; await c.Response.WriteAsJsonAsync(ApiResponse.Fail("Cần đăng nhập; JWT thiếu, hết hạn hoặc không hợp lệ.")); },
        OnForbidden = async c => { c.Response.StatusCode = 403; await c.Response.WriteAsJsonAsync(ApiResponse.Fail("Không có quyền truy cập.")); }
    };
});
builder.Services.AddAuthorization(); builder.Services.AddHttpContextAccessor(); builder.Services.AddDataProtection();
builder.Services.AddHostedService<SessionExpiryWorker>();
builder.Services.AddScoped<AuthService>(); builder.Services.AddScoped<SmtpEmailService>(); builder.Services.AddScoped<AccessService>(); builder.Services.AddScoped<AttendanceService>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.Configure<ApiBehaviorOptions>(o => o.InvalidModelStateResponseFactory = c => new BadRequestObjectResult(ApiResponse.Fail("Dữ liệu không hợp lệ.", c.ModelState.Where(x => x.Value!.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "Giá trị không hợp lệ." : e.ErrorMessage)))));
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("otp", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("verify", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (c, t) => { c.HttpContext.Response.StatusCode = 429; await c.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail("Quá nhiều yêu cầu. Thử lại sau 60 giây."), t); };
});
builder.Services.AddEndpointsApiExplorer(); builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Student Attendance API", Version = "v1" });
    o.AddSecurityDefinition("Bearer", new() { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Nhập accessToken sau khi xác minh OTP." });
    o.SchemaFilter<AttendanceApi.Services.EnumSchemaFilter>();
    o.OperationFilter<AttendanceApi.Services.ErrorResponseFilter>();
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (Exception ex) when (!context.Response.HasStarted)
    {
        var sql = (ex as DbUpdateException)?.InnerException as SqlException ?? ex as SqlException;
        var status = ex is ApiException a ? a.Status : ex is DbUpdateConcurrencyException ? 409 : sql?.Number is 2601 or 2627 or 547 or 1205 ? 409 : 500;
        var message = ex is ApiException b ? b.Message : status == 409 ? "Dữ liệu trùng, đang được sử dụng hoặc đã thay đổi. Tải lại và thử lại." : "Lỗi máy chủ. Kiểm tra cấu hình SQL Server và nhật ký backend.";
        if (status >= 500) app.Logger.LogError("Request failed: {ExceptionType}; SQL number: {SqlNumber}", ex.GetType().Name, sql?.Number);
        context.Response.Clear(); context.Response.StatusCode = status; await context.Response.WriteAsJsonAsync(ApiResponse.Fail(message, (ex as ApiException)?.Errors, (ex as ApiException)?.DataValue));
    }
});
app.UseStatusCodePages(async c => { await c.HttpContext.Response.WriteAsJsonAsync(ApiResponse.Fail(c.HttpContext.Response.StatusCode switch { 404 => "Không tìm thấy endpoint.", 405 => "Phương thức không được hỗ trợ.", _ => "Yêu cầu thất bại." })); });
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
if (args.Contains("--seed") || args.Contains("--seed-admin"))
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Seed chỉ được phép trong Development.");
    using var scope = app.Services.CreateScope();
    if (args.Contains("--seed-admin"))
        await DemoSeeder.SeedAdmin(scope.ServiceProvider, builder.Configuration);
    else
        await DemoSeeder.Seed(scope.ServiceProvider, builder.Configuration);
    Console.WriteLine("Seed hoàn tất; đăng nhập vẫn yêu cầu OTP email thật.");
    return;
}
app.Run();
public partial class Program;
