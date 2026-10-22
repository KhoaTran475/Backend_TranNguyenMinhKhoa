# Kết quả kiểm tra

Ngày kiểm tra: 22/10/2026 (Asia/Saigon).

## Đã chạy thành công

- `dotnet restore trannguyenminhkhoa_webAPI/trannguyenminhkhoa_webAPI.csproj` và `dotnet restore tests/AttendanceChecks/AttendanceChecks.csproj`.
- `dotnet tool restore`: dotnet-ef 8.0.3.
- `dotnet ef migrations add InitialCreate --project trannguyenminhkhoa_webAPI --output-dir Data/Migrations`.
- `dotnet build trannguyenminhkhoa_webAPI.sln --no-restore`: **0 lỗi, 0 cảnh báo**.
- `dotnet run --project tests/AttendanceChecks --no-build -- --sql`: **76 phép kiểm thành công** với SQL Server thật và HTTP API thật, bao gồm seed riêng Admin chạy hai lần không tạo trùng.

Lần kiểm tra cuối dùng database riêng `AttendanceCheck_348946f9ec6f4ac8bd9a2d39b0e1aed8`, được tạo bởi runner và đã xóa sau kiểm tra. Kiểm thử không apply migration vào database có sẵn của người dùng. Runner luôn sinh tên mới và chỉ dọn database mang tên GUID do chính lần chạy đó tạo.

## Các luồng đã kiểm tra

- Haversine tại cùng vị trí và khoảng cách chuẩn; trước giờ, đúng giờ, đúng ngưỡng trễ, sau ngưỡng, đúng giờ đóng.
- Apply migration SQL Server, seed hai lần không trùng dữ liệu; toàn bộ foreign key Restrict.
- Response lỗi 400/401/403/404/409/429/503, pagination/search và trường bool bắt buộc.
- SMTP thiếu cấu hình trả 503 có challenge khôi phục, không cấp JWT, resend tạo challenge mới.
- OTP đúng purpose, hết hạn, dùng một lần, 5 lần sai, cooldown 60 giây; enum trong response lỗi giữ dạng chuỗi.
- Đăng ký đòi hồ sơ có sẵn, liên kết tài khoản, chỉ cấp Student dù gửi role Admin, xác minh email.
- Admin/Lecturer/Student, Student không đọc tài nguyên Admin; Lecturer không xem lớp của giảng viên khác; Student ngoài lớp không điểm danh.
- Mở phiên, QR ổn định khi còn hạn, một QR dùng cho nhiều sinh viên, QR sai, GPS ngoài bán kính và accuracy kém bị từ chối.
- Hai HTTP check-in đồng thời chỉ một thành công; unique constraint SQL từ chối bản ghi trùng trực tiếp.
- Present/Late theo giờ server; đóng phiên sinh Absent, không mở lại, QR sau đóng hết hiệu lực.
- Điều chỉnh trạng thái bắt buộc lý do, log lưu trạng thái trước/sau; không xóa sinh viên đã dùng.
- Khóa tài khoản vô hiệu JWT đang dùng; mở lại không khôi phục token cũ.
- Swagger có response envelope, enum dạng chuỗi, loại navigation object; auth login không yêu cầu JWT, check-in yêu cầu Bearer.

## Giới hạn kiểm thử

**Chưa gửi/nhận OTP qua SMTP thật và chưa chạy Conveyor/Flutter trên điện thoại.** Không có SMTP credentials hoặc tunnel URL được cấu hình trong phiên này.

Để kiểm tra verify/JWT và nghiệp vụ mà không gửi email, runner chèn các OTP fixture có HMAC vào database tạm rồi gọi API xác minh thật. Đây là dữ liệu thử nghiệm riêng; runtime không có API tiết lộ mã hoặc bypass OTP. Việc chèn fixture không chứng minh email delivery.

Worker đóng phiên tự động, toàn bộ biến thể CRUD, timeout SMTP và khả năng chống mọi hình thức gian lận chưa được kiểm thử toàn diện. QR+GPS chỉ là kiểm tra hỗ trợ. File attendance.http và README cung cấp các bước demo thủ công sau khi cấu hình SMTP, email seed, SQL và Conveyor.

## Chạy lại

```powershell
dotnet build .\trannguyenminhkhoa_webAPI.sln
dotnet run --project .\tests\AttendanceChecks
# SQL Server localhost, cần quyền tạo/xóa database tạm:
dotnet run --project .\tests\AttendanceChecks -- --sql
```

Có thể đặt `ATTENDANCE_TEST_SQL` cho server/credentials thử nghiệm khác. Runner thay InitialCatalog bằng tên database tạm tự sinh; không dùng tên database lấy từ cấu hình. Không chạy trên server production.
