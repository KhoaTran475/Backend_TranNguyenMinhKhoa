using AttendanceApi.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace AttendanceApi.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Student> Students => Set<Student>(); public DbSet<Lecturer> Lecturers => Set<Lecturer>(); public DbSet<Subject> Subjects => Set<Subject>(); public DbSet<Room> Rooms => Set<Room>(); public DbSet<CourseClass> Classes => Set<CourseClass>(); public DbSet<Enrollment> Enrollments => Set<Enrollment>(); public DbSet<ClassSession> Sessions => Set<ClassSession>(); public DbSet<Models.Attendance> Attendances => Set<Models.Attendance>(); public DbSet<AttendanceChangeLog> AttendanceChanges => Set<AttendanceChangeLog>(); public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<Student>().HasIndex(x => x.StudentCode).IsUnique(); b.Entity<Student>().HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.Entity<Lecturer>().HasIndex(x => x.LecturerCode).IsUnique(); b.Entity<Lecturer>().HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.Entity<Subject>().HasIndex(x => x.Code).IsUnique(); b.Entity<Room>().HasIndex(x => x.Code).IsUnique(); b.Entity<CourseClass>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Enrollment>().HasKey(x => new { x.CourseClassId, x.StudentId });
        b.Entity<Models.Attendance>().HasIndex(x => new { x.ClassSessionId, x.StudentId }).IsUnique();
        b.Entity<EmailChallenge>().HasIndex(x => new { x.UserId, x.Purpose });
        b.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
        foreach (var e in b.Model.GetEntityTypes()) foreach (var fk in e.GetForeignKeys()) fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
