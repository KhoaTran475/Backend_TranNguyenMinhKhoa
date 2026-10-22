using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using AttendanceApi.Models;
namespace AttendanceApi.Services;

public class SmtpEmailService(IConfiguration config)
{
    public async Task Send(string to, string otp, ChallengePurpose purpose)
    {
        var c = config.GetSection("Smtp");
        if (string.IsNullOrWhiteSpace(c["Host"]) || string.IsNullOrWhiteSpace(c["Sender"]) || !Enum.TryParse<SecureSocketOptions>(c["Security"], out var security) || security is not (SecureSocketOptions.StartTls or SecureSocketOptions.SslOnConnect)) throw new InvalidOperationException("SMTP requires Host, Sender and StartTls/SslOnConnect.");
        using var client = new SmtpClient { Timeout = 15000 };
        var msg = new MimeMessage(); msg.From.Add(MailboxAddress.Parse(c["Sender"]!)); msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = purpose == ChallengePurpose.Login ? "OTP đăng nhập điểm danh" : "OTP xác minh email điểm danh";
        msg.Body = new TextPart("plain") { Text = $"Mã OTP của bạn: {otp}. Hết hạn sau 5 phút. Không chia sẻ mã." };
        await client.ConnectAsync(c["Host"]!, c.GetValue<int>("Port"), security);
        if (!string.IsNullOrWhiteSpace(c["Username"])) await client.AuthenticateAsync(c["Username"]!, c["Password"] ?? "");
        await client.SendAsync(msg); await client.DisconnectAsync(true);
    }
}
