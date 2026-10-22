# Backend điểm danh sinh viên — ASP.NET Core .NET 8

Đã triển khai backend trong project `trannguyenminhkhoa_webAPI`: Controllers, SQL Server, EF Core Code First, Identity, JWT Bearer, OTP email bằng SMTP, phân quyền Admin/Lecturer/Student, QR và GPS. Không có trang quản trị web; dashboard nằm trong Flutter. Tài liệu Word Tuần 6/7 chỉ dùng tham khảo tổ chức EF/Identity và kết nối Conveyor; yêu cầu trong tệp văn bản của người dùng quyết định phạm vi triển khai.

## Chuẩn bị và mở project

- Visual Studio 2022 hỗ trợ .NET 8, workload ASP.NET and web development; .NET 8 SDK/runtime (SDK mới có thể build target net8.0).
- SQL Server đang chạy; nên có SSMS để kiểm tra. Dùng database mới, riêng cho bài demo.
- Hộp thư SMTP thật và credentials do bạn quản lý.
- Conveyor trong Visual Studio; điện thoại/máy ảo có mạng, quyền camera và location.

Mở **trannguyenminhkhoa_webAPI.sln**. Solution này chứa API và chương trình kiểm tra. File `.slnx` ban đầu vẫn mở được trên Visual Studio mới. Từ PowerShell tại thư mục solution:

```powershell
dotnet restore .\trannguyenminhkhoa_webAPI.sln
dotnet tool restore
dotnet build .\trannguyenminhkhoa_webAPI.sln
$apiProject = '.\trannguyenminhkhoa_webAPI\trannguyenminhkhoa_webAPI.csproj'
```

Các package Microsoft được yêu cầu dùng đúng 8.0.3. Công cụ EF local cũng ghim 8.0.3. MailKit 4.17.0 tương thích .NET 8, thay bản cũ có cảnh báo bảo mật; [trang package chính thức](https://www.nuget.org/packages/MailKit/4.17.0). Swashbuckle 6.6.2.

## SQL Server và JWT

Connection string được cấu hình trực tiếp trong `trannguyenminhkhoa_webAPI/appsettings.json`: server `localhost`, database `trannguyenminhkhoa`, Windows Authentication. Giá trị tương ứng trong User Secrets đã được xóa để dùng cấu hình JSON. JWT, SMTP password và mật khẩu seed vẫn cấu hình bằng User Secrets trong Development hoặc biến môi trường:

```powershell
$jwtBytes = New-Object byte[] 48
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($jwtBytes)
$rng.Dispose()
$jwtSecret = [Convert]::ToBase64String($jwtBytes)
dotnet user-secrets set 'Jwt:Secret' $jwtSecret --project $apiProject
```

Thay `Server=localhost` bằng instance của bạn, ví dụ `.\SQLEXPRESS`. Nếu có LocalDB: `Server=(localdb)\MSSQLLocalDB;Database=trannguyenminhkhoa;Trusted_Connection=True;TrustServerCertificate=True`. Nếu dùng SQL Authentication, để `User Id=...;Password=...` trong User Secrets. `TrustServerCertificate=True` dành cho máy demo có certificate tự ký; server thật nên dùng certificate tin cậy.

JWT secret phải ngẫu nhiên tối thiểu 32 byte. API từ chối chạy nếu thiếu secret. Mặc định Issuer `AttendanceApi`, Audience `AttendanceFlutter`; nếu đổi phải cấu hình đồng bộ. JWT sống 60 phút, hết hạn phải đăng nhập OTP lại, không có refresh token.

Biến môi trường tương ứng: `ConnectionStrings__DefaultConnection`, `Jwt__Secret`, `Smtp__Host`, `Smtp__Port`, `Smtp__Security`, `Smtp__Username`, `Smtp__Password`, `Smtp__Sender`, `DemoSeed__Password`, `DemoSeed__AdminEmail`, v.v. Khởi động lại API sau khi đổi môi trường. Không chuyển SQL/SMTP/JWT signing secret sang Flutter hoặc commit vào source.

## SMTP gửi OTP thật

Đã có script nhập App Password ẩn trên máy: chạy `.\scripts\Configure-GmailSmtp.ps1` từ thư mục solution (mặc định email Admin; đổi bằng `-Email dia-chi-cua-ban@gmail.com`). Script lưu SMTP vào User Secrets bằng dotnet CLI, không ghi mật khẩu vào source hoặc lịch sử lệnh PowerShell. Cần tự tạo Gmail App Password trong tài khoản Google đã bật xác minh hai bước, nhập khi script yêu cầu, rồi khởi động lại API và gọi `/api/auth/login` để kiểm tra email OTP thật. Không gửi App Password vào chat.

Ví dụ Gmail port 587 + StartTls:

```powershell
dotnet user-secrets set 'Smtp:Host' 'smtp.gmail.com' --project $apiProject
dotnet user-secrets set 'Smtp:Port' '587' --project $apiProject
dotnet user-secrets set 'Smtp:Security' 'StartTls' --project $apiProject
dotnet user-secrets set 'Smtp:Username' '<gmail-cua-ban>' --project $apiProject
dotnet user-secrets set 'Smtp:Password' '<app-password>' --project $apiProject
dotnet user-secrets set 'Smtp:Sender' '<gmail-cua-ban>' --project $apiProject
```

Bật xác minh hai bước cho Google Account rồi tạo App Password, dùng mật khẩu ứng dụng này cho SMTP. Một số tài khoản tổ chức/Advanced Protection không có tùy chọn đó; dùng nhà cung cấp SMTP phù hợp. Xem [hướng dẫn Google](https://support.google.com/accounts/answer/185833?hl=vi). Với port 465, chọn `SslOnConnect`. Backend chỉ chấp nhận `StartTls` hoặc `SslOnConnect` và dùng xác minh certificate mặc định.

Không có chế độ giả gửi thành công hoặc log OTP. Khi SMTP chưa cấu hình/gửi lỗi, API trả **503 cùng Challenge trong data**. Lấy challengeId từ response, sửa SMTP rồi gọi `/auth/resend-otp`; không đăng ký lại. SMTP lỗi cho phép resend ngay; gửi thành công áp dụng cooldown 60 giây. Mỗi mã sống 5 phút, dùng một lần, tối đa 5 lần nhập sai, resend hủy mã cũ. JWT chỉ cấp sau OTP Login đúng.

## Migration

Đã có `InitialCreate` và ModelSnapshot trong `Data/Migrations`. API không tự apply migration hay reset dữ liệu khi chạy. Kiểm tra connection string trỏ đúng database demo mới rồi chạy:

```powershell
dotnet ef database update --project .\trannguyenminhkhoa_webAPI
```

Visual Studio Package Manager Console: chọn API làm Startup Project và Default project, chạy `Update-Database`. Khi thay mô hình về sau: `Add-Migration TenThayDoi`, rồi `Update-Database`; CLI tương ứng `dotnet ef migrations add TenThayDoi --project .\trannguyenminhkhoa_webAPI`.

DesignTimeFactory đọc appsettings/User Secrets/environment để EF hoạt động khi JWT chưa cấu hình. EF có thể in cảnh báo JWT rồi tiếp tục bằng factory; runtime vẫn yêu cầu secret. Không apply migration vào database bài tập khác hoặc database có sẵn không liên quan.

## Seed demo

Để chỉ tạo tài khoản quản trị ban đầu, cấu hình `DemoSeed:AdminEmail` và `DemoSeed:Password` rồi chạy `dotnet run --project .\trannguyenminhkhoa_webAPI -- --seed-admin` trong Development. Lệnh này tạo `admin.demo` và ba role cần thiết, không tạo thêm tài khoản Lecturer/Student hay dữ liệu lớp. Chạy lại không đổi mật khẩu/email tài khoản có sẵn. Admin vẫn phải nhận OTP email thật khi đăng nhập.

Seed chỉ chạy khi truyền `--seed`, chỉ cho phép Development, sau Update-Database. Cần 5 email khác nhau **do bạn quản lý**; có thể dùng alias nếu nhà cung cấp hỗ trợ. Không tự gán email người khác.

```powershell
dotnet user-secrets set 'DemoSeed:Password' '<mat-khau-demo-manh>' --project $apiProject
dotnet user-secrets set 'DemoSeed:AdminEmail' '<email-admin-cua-ban>' --project $apiProject
dotnet user-secrets set 'DemoSeed:LecturerEmail' '<email-lecturer-cua-ban>' --project $apiProject
dotnet user-secrets set 'DemoSeed:Student1Email' '<email-sv1-cua-ban>' --project $apiProject
dotnet user-secrets set 'DemoSeed:Student2Email' '<email-sv2-cua-ban>' --project $apiProject
dotnet user-secrets set 'DemoSeed:Student3Email' '<email-sv3-cua-ban>' --project $apiProject
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project .\trannguyenminhkhoa_webAPI -- --seed
```

Mật khẩu tối thiểu 8 ký tự, có hoa, thường, số, ký tự đặc biệt. Seed tạo:

| Dữ liệu | Giá trị |
|---|---|
| Admin | admin.demo |
| Lecturer | lecturer.demo, GV001 |
| Student | student1.demo, student2.demo, student3.demo; SV001..SV003 |
| Môn học | LTDD, WEBAPI |
| Phòng | DEMO101 |
| Lớp học phần | LTDD-DEMO, giảng viên GV001, đủ 3 enrollment |
| Buổi đầu | Scheduled, start = giờ seed trừ 1 phút, đóng sau 3 giờ, hết buổi sau 4 giờ, ngưỡng trễ 15 phút |

Tài khoản demo đã xác minh email, nhưng **mọi lần đăng nhập vẫn nhận OTP email thật**. Seed idempotent, không đổi mật khẩu/email/hồ sơ hay reset dữ liệu khi chạy lại. Buổi theo thời gian hiện tại chỉ được tạo khi lớp demo được tạo lần đầu. Ngày khác Lecturer tạo buổi mới, không seed để dời lịch.

Đặt email và mật khẩu seed trước lần chạy đầu. Sau khi đã seed, đổi Secrets không đổi tài khoản có sẵn; email không khớp báo lỗi để tránh ghi đè. Muốn bộ demo khác: chọn database demo mới, migrate và seed; giữ nguyên database cũ. Không có API đổi email/reset password trong phạm vi này.

Phòng demo: **latitude 10.762622, longitude 106.660172, radius 100 m**. Đặt cùng tọa độ trên emulator. GPS cần accuracy >0 và <=50 m. Nếu demo ở địa điểm khác, tạo phòng với tọa độ thực tế trước khi tạo buổi; phòng đã dùng không cho sửa tọa độ.

## Chạy Visual Studio → Conveyor → Flutter

1. Mở `.sln`, chọn API làm Startup Project, profile `https`, F5/Ctrl+F5.
2. Swagger mặc định: `https://localhost:7119/swagger` theo launchSettings. IIS Express dùng port riêng Visual Studio hiển thị.
3. POST `/api/auth/login` → nhận challengeId → nhận OTP email → POST `/api/auth/verify-login-otp` → accessToken. Swagger Authorize: dán token nguyên chuỗi, Swagger tự thêm Bearer.
4. Chạy Conveyor cho project, bật URL Remote/Internet HTTPS theo bản đã cài. Giữ API và tunnel chạy. Kiểm tra URL công khai từ điện thoại.
5. Flutter `.env`: **`BASE_URL=https://<conveyor-host>/api/`**, có `/` cuối. Không dùng localhost trong Flutter. Tunnel đổi hostname thì cập nhật `.env` và restart app.
6. Flutter gọi API; chỉ backend kết nối SQL Server. Flutter native không cần CORS. App không chứa SQL connection string, SMTP password hoặc JWT signing secret.

Gia tốc xử lý riêng trên Flutter, không phải điều kiện điểm danh và không có API lưu. Không có nhận diện khuôn mặt, vân tay, video call, dịch thuật hay AI.

## Demo theo vai trò

1. Admin đăng nhập password + OTP, xem dashboard/users/hồ sơ/lớp/enrollment. Seed đã đủ SV. Muốn demo đăng ký mới: Admin tạo hồ sơ Student với mã/email do bạn quản lý; Student register → verify-email → login → verify-login-otp.
2. Lecturer đăng nhập OTP, xem `/me/classes` và `/me/sessions`. Nếu buổi seed đã hết giờ thì tạo buổi mới. Mở phiên trong giờ, GET `/sessions/{id}/qr`, Flutter hiển thị qrPayload.
3. Student đăng nhập OTP, GET `/auth/me` và `/me/sessions`, quét QR, lấy GPS mới, POST `/attendance/check-in`. Không gửi studentId. Gọi lại sẽ 409.
4. Thử tọa độ ngoài phòng/accuracy >50 để thấy lỗi 400. QR còn hạn dùng cho nhiều SV; lấy lại QR còn hạn trả cùng payload, không làm QR đang chiếu mất hiệu lực.
5. Lecturer xem attendance và đóng phiên; SV thiếu thành Absent. Không mở lại. Sửa Present/Late/Absent/Excused cần reason, xem lịch sử ở `/attendance/{id}/changes`.
6. Admin khóa Student: JWT cũ lập tức 401. Mở lại vẫn phải login OTP mới. Student chỉ xem dữ liệu mình; Lecturer khác không truy cập lớp không được giao.

QR+GPS hỗ trợ kiểm tra, không đảm bảo chặn mọi gian lận. QR tối đa 2 phút. Giữ Data Protection key của tài khoản Windows chạy API để lấy lại QR đang hiệu lực sau restart. Worker tự đóng phiên quá giờ mỗi 15 giây; API dừng thì xử lý khi chạy lại. Tại giờ đóng, check-in bị từ chối ngay dù worker chưa chạy.

## Kiểm tra và bàn giao

- [API_CONTRACT.md](API_CONTRACT.md): đủ request/response, kiểu, nullable, enum, query, role và ví dụ.
- [attendance.http](trannguyenminhkhoa_webAPI/attendance.http): kiểm thử thủ công; thay các biến bằng token/challenge/OTP của bạn, không commit giá trị thật.
- `dotnet run --project tests/AttendanceChecks`: kiểm tra Haversine và biên thời gian, không cần database.
- `dotnet run --project tests/AttendanceChecks -- --sql`: tạo database tạm `AttendanceCheck_<GUID>` trên SQL Server localhost, migrate, seed hai lần, kiểm tra HTTP, xóa đúng database tạm. Cần quyền tạo/xóa database trên server thử nghiệm. Có thể đặt `ATTENDANCE_TEST_SQL` cho server/credentials khác; tên database luôn do runner sinh, không lấy từ cấu hình.

Test SQL chèn OTP fixtures vào database tạm để kiểm tra verify/JWT mà không gửi mail. API thực tế không có endpoint bypass OTP. Kết quả và giới hạn kiểm thử ở [VERIFICATION.md](VERIFICATION.md). SMTP delivery và Conveyor cần thử thật sau cấu hình.

Lỗi SQL: kiểm tra instance/service/credentials và Update-Database. Lỗi SMTP: kiểm tra host/port/TLS/sender/App Password/firewall, rồi resend bằng challengeId từ lỗi. OTP 429: chờ resendAvailableAtUtc hoặc quota phút kế tiếp. Đổi JWT secret làm OTP challenge cũ không xác minh được; yêu cầu challenge mới.

Mã nguồn tổ chức `Controllers`, `Data`, `Models`, `DTOs`, `Services`; không dùng generic repository, CQRS hay microservices.
