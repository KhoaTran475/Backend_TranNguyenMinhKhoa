using AttendanceApi.Data;
using AttendanceApi.Models;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Services;
// Closing by server time does not depend on the lecturer keeping the app open.
public class SessionExpiryWorker(IServiceScopeFactory scopes, ILogger<SessionExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); var service = scope.ServiceProvider.GetRequiredService<AttendanceService>(); var now = DateTimeOffset.UtcNow;
                var ids = await db.Sessions.Where(x => x.AttendanceState == AttendanceState.Open && x.AttendanceClosesAtUtc <= now).Select(x => x.Id).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    await using var tx = await db.Database.BeginTransactionAsync(stoppingToken);
                    var cid = await db.Sessions.Where(x => x.Id == id).Select(x => x.CourseClassId).SingleAsync(stoppingToken);
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Classes] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={cid}", stoppingToken);
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Sessions] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={id}", stoppingToken);
                    var s = await db.Sessions.SingleAsync(x => x.Id == id, stoppingToken);
                    if (s.AttendanceState == AttendanceState.Open) await service.CloseCore(s);
                    await tx.CommitAsync(stoppingToken); db.ChangeTracker.Clear();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Không tự đóng phiên: {Type}. Kiểm tra SQL Server.", ex.GetType().Name); }
        }
    }
}
