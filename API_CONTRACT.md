# Hợp đồng API điểm danh v1

Base URL Flutter: **`https://<conveyor-host>/api/`**. Các đường dẫn trong tài liệu tương đối với `/api`. Gửi JSON với `Content-Type: application/json`. Endpoint có đăng nhập nhận `Authorization: Bearer <accessToken>`.

## Quy ước và kiểu dữ liệu

JSON camelCase, enum dạng chuỗi. `int` là int32; ID nghiệp vụ >0, userId là Identity string, challengeId là UUID string. `number` là double hữu hạn. `UTC` là string ISO 8601 UTC, ví dụ `2026-10-22T02:00:00Z` hoặc `+00:00`. Backend dùng thời gian server để quyết định điểm danh. `?` sau kiểu trong bảng nghĩa là có thể null; các trường còn lại không null.

- Role: `Admin`, `Lecturer`, `Student`.
- AttendanceState: `Scheduled`, `Open`, `Closed`.
- AttendanceStatus: `Present`, `Late`, `Absent`, `Excused`.
- ChallengePurpose: `VerifyEmail`, `Login`.

Client dùng đúng tên enum trên; enum dạng số bị từ chối. Không có refresh token.

## Envelope và HTTP

Thành công: `{"success":true,"message":"Thành công.","data":D,"errors":null}`.
Thất bại: `{"success":false,"message":"...","data":null,"errors":null}`.
POST tạo mới dùng 201; các thao tác khác dùng 200. DELETE trả envelope với data null. Chi tiết/QR/dashboard không phân trang.

| HTTP | Ý nghĩa |
|---|---|
| 400 | JSON/validation/enum sai, OTP không hợp lệ/hết hạn/đã dùng, QR sai, vị trí không đạt |
| 401 | Thiếu JWT, token hết hạn/thu hồi, sai password hoặc đăng nhập bị khóa |
| 403 | Sai role/quyền sở hữu/lớp, tài khoản bị khóa khi OTP, email chưa xác minh khi login |
| 404 | Endpoint hoặc tài nguyên không tồn tại |
| 405 | Phương thức không hỗ trợ |
| 409 | Trùng dữ liệu/check-in, dữ liệu đã dùng, trạng thái không cho phép, xung đột đồng thời |
| 429 | Quota hoặc cooldown OTP |
| 500 | Lỗi máy chủ/SQL chưa cấu hình |
| 503 | SMTP thiếu/lỗi hoặc không đọc được khóa QR hiện hành |

Lỗi SMTP, cooldown, email chưa xác minh có thể trả **data:Challenge**, để frontend tiếp tục resend/verify. Lỗi validation có errors là object ánh xạ tên trường sang string[]. Lỗi Identity có errors là array `{code:string,description:string}`. Các lỗi khác errors null. Không dùng message làm mã phân quyền.

Ví dụ thành công:
```json
{"success":true,"message":"Thành công.","data":{"id":1,"code":"LTDD","name":"Lập trình di động"},"errors":null}
```
Ví dụ validation (400):
```json
{"success":false,"message":"Dữ liệu không hợp lệ.","data":null,"errors":{"Email":["The Email field is not a valid e-mail address."]}}
```
Ví dụ JWT (401):
```json
{"success":false,"message":"Cần đăng nhập; JWT thiếu, hết hạn hoặc không hợp lệ.","data":null,"errors":null}
```
Ví dụ lỗi SMTP (503):
```json
{"success":false,"message":"SMTP chưa được cấu hình hoặc gửi email thất bại. Cấu hình SMTP rồi gửi lại với challengeId này.","data":{"challengeId":"7ab71950-104a-4ae7-b8eb-245f4d5b89e1","purpose":"Login","maskedEmail":"s***@example.com","expiresAtUtc":"2026-10-22T02:05:00Z","resendAvailableAtUtc":"2026-10-22T02:00:00Z"},"errors":null}
```

## Query danh sách

Mọi GET danh sách nhận `search?:string(max150)`, `page?:int(default1,min1)`, `pageSize?:int(default20,1..100)`. Response data là `Page<T> = {items:T[],totalCount:int,page:int,pageSize:int}`. Không có dữ liệu thì items rỗng, totalCount 0. GET chi tiết không có dữ liệu trả 404.

```json
{"success":true,"message":"Thành công.","data":{"items":[],"totalCount":0,"page":1,"pageSize":20},"errors":null}
```

Search: hồ sơ theo mã/tên, môn/phòng/lớp theo code/name, buổi theo code/name lớp (danh sách buổi trong lớp tìm thêm tên phòng), attendance buổi theo mã/tên SV, `/me/attendance` theo code/name lớp, lịch sử sửa theo reason, users theo username/fullName/email.

## Request schema

Tất cả trường trong bảng bắt buộc, không null, kể cả int/bool/enum/thời gian. String Required không chấp nhận rỗng hoặc whitespace. Thiếu trường hoặc sai validation trả 400. Trường thừa được bỏ qua; không gửi role/UserId/hash/navigation property.

| Schema | Trường và ràng buộc |
|---|---|
| RegisterRequest | studentCode:string(max40), fullName:string(max150), email:string(email,max256), username:string(max100), password:string(8..128) |
| LoginRequest | username:string(max100), password:string(max128) |
| VerifyOtpRequest | challengeId:UUID, otp:string(đúng 6 chữ số) |
| ResendRequest | challengeId:UUID |
| StudentRequest | studentCode:string(max40), fullName:string(max150), email:string(email,max256) |
| LecturerRequest | lecturerCode:string(max40), fullName:string(max150), email:string(email,max256) |
| SubjectRequest | code:string(max40), name:string(max150) |
| RoomRequest | code:string(max40), name:string(max150), latitude:number(-90..90), longitude:number(-180..180), allowedRadiusMeters:number(1..5000) |
| ClassRequest | code:string(max40), name:string(max150), subjectId:int, lecturerId:int |
| EnrollmentRequest | studentId:int |
| SessionRequest | roomId:int, startAtUtc:UTC, endAtUtc:UTC, attendanceClosesAtUtc:UTC, lateAfterMinutes:int(0..240) |
| CheckInRequest | sessionId:int, qrToken:string(max256), latitude:number(-90..90), longitude:number(-180..180), accuracyMeters:number(0.01..50) |
| AttendanceUpdateRequest | status:AttendanceStatus, reason:string(max1000) |
| CreateLecturerUserRequest | lecturerId:int, username:string(max100), password:string(8..128) |
| ActiveRequest | isActive:boolean |

Mật khẩu Identity cần hoa, thường, chữ số, ký tự đặc biệt, ít nhất 8 ký tự. Session yêu cầu offset UTC=0, `startAtUtc < attendanceClosesAtUtc <= endAtUtc` và `startAtUtc + lateAfterMinutes <= attendanceClosesAtUtc`.

## Response data schema

Các trường dưới đây luôn được xuất; trường nullable dùng giá trị null, không bị bỏ qua. Không xuất navigation object, password hash, security stamp, OTP hay QR hash.

| Schema | Trường |
|---|---|
| Student | id:int, studentCode:string, fullName:string, email:string, userId:string? |
| Lecturer | id:int, lecturerCode:string, fullName:string, email:string, userId:string? |
| Subject | id:int, code:string, name:string |
| Room | id:int, code:string, name:string, latitude:number, longitude:number, allowedRadiusMeters:number |
| CourseClass | id:int, code:string, name:string, subjectId:int, lecturerId:int |
| Enrollment | courseClassId:int, studentId:int |
| Session | id:int, courseClassId:int, roomId:int, startAtUtc:UTC, endAtUtc:UTC, attendanceClosesAtUtc:UTC, lateAfterMinutes:int, attendanceState:AttendanceState |
| Attendance | id:int, sessionId:int, studentId:int, status:AttendanceStatus, checkedInAtUtc:UTC?, latitude:number?, longitude:number?, accuracyMeters:number?, distanceMeters:number? |
| AttendanceChange | id:int, attendanceId:int, changedByUserId:string, changedAtUtc:UTC, previousStatus:AttendanceStatus, newStatus:AttendanceStatus, reason:string |
| User | id:string, username:string, fullName:string, email:string, role:Role, studentId:int?, lecturerId:int? |
| UserAccount | user:User, isActive:boolean, emailConfirmed:boolean |
| Challenge | challengeId:UUID, purpose:ChallengePurpose, maskedEmail:string, expiresAtUtc:UTC, resendAvailableAtUtc:UTC |
| Login | accessToken:string, expiresAtUtc:UTC, user:User |
| Qr | sessionId:int, qrPayload:string(JSON), expiresAtUtc:UTC |
| EmailVerified | emailVerified:boolean(true) |
| Active | id:string, isActive:boolean |
| Dashboard | role:Role, classCount:int, sessionCount:int, openSessionCount:int, presentCount:int, lateCount:int, absentCount:int, excusedCount:int, studentCount:int?, lecturerCount:int?, userCount:int? |

Absent tự sinh có checkedInAtUtc và vị trí null. Điều chỉnh trạng thái giữ nguyên thông tin check-in gốc. CourseClass là lớp học phần, Session là buổi học, Enrollment là danh sách SV thuộc lớp.

## Auth

Năm endpoint POST không cần JWT. GET me yêu cầu JWT mọi role.

| Method / path | Request | Data / HTTP |
|---|---|---|
| POST /auth/register | RegisterRequest | Challenge / 201 |
| POST /auth/verify-email | VerifyOtpRequest | EmailVerified / 200 |
| POST /auth/resend-otp | ResendRequest | Challenge / 200 |
| POST /auth/login | LoginRequest | Challenge / 200, chưa có JWT |
| POST /auth/verify-login-otp | VerifyOtpRequest | Login / 200 |
| GET /auth/me | không body | User / 200 |

Đăng ký phải khớp Student Admin tạo theo studentCode và email, hồ sơ chưa liên kết. User mới chỉ nhận Student. Tạo user/role/liên kết trong cùng giao dịch; thất bại liên kết không để tài khoản rác. SMTP lỗi sau tạo tài khoản: 503 + Challenge, dùng resend, không register lại.

Tài khoản chưa xác minh email login đúng password trả 403 + Challenge purpose VerifyEmail. Gọi verify-email, rồi login password để nhận Challenge Login và verify-login-otp. Tài khoản Lecturer do Admin tạo đi theo luồng này. Tài khoản seed đã xác minh email, vẫn cần OTP Login thật.

OTP ngẫu nhiên mật mã, lưu HMAC gắn challengeId, không lưu/log mã rõ. OTP 5 phút, dùng một lần, tối đa 5 lần sai. Challenge khác purpose không dùng thay nhau. Resend tối thiểu 60 giây sau gửi thành công; trả challengeId **mới** và hủy mã cũ. Frontend cập nhật toàn bộ Challenge. SMTP lỗi cho gửi lại ngay.

Quota theo IP kết nối: register/login/resend dùng chung 10 request/phút; verify dùng chung 30/phút. Sai password 5 lần khóa đăng nhập 5 phút. Không tin X-Forwarded-For tùy ý; sau Conveyor các thiết bị có thể dùng chung quota IP.

JWT 60 phút có userId và role. Mọi request kiểm tra IsActive, EmailConfirmed và security stamp. Khóa hoặc mở khóa làm token cũ hết hiệu lực, cần login lại. 401 thì xóa token client và về đăng nhập. Không có refresh token.

Ví dụ login:
```json
{"username":"student1.demo","password":"<mat-khau-da-cau-hinh>"}
```
```json
{"success":true,"message":"Đã gửi OTP; chưa cấp JWT.","data":{"challengeId":"7ab71950-104a-4ae7-b8eb-245f4d5b89e1","purpose":"Login","maskedEmail":"s***@example.com","expiresAtUtc":"2026-10-22T02:05:00Z","resendAvailableAtUtc":"2026-10-22T02:01:00Z"},"errors":null}
```
Verify gửi `{"challengeId":"7ab71950-104a-4ae7-b8eb-245f4d5b89e1","otp":"123456"}` (mã ví dụ không dùng được).

## CRUD và quản lý lớp

A=Admin, L=Lecturer, S=Student. L chỉ truy cập lớp được phân công; S chỉ truy cập lớp đã enrollment. Backend kiểm tra quyền sở hữu, không chỉ role. Admin có quyền toàn bộ.

| Resource | GET list -> Page<T> | GET /{id} -> T | POST body -> T (201) | PUT /{id} body -> T | DELETE /{id} -> null | Quyền |
|---|---|---|---|---|---|---|
| /students | Student | Student | StudentRequest | StudentRequest | có | A |
| /lecturers | Lecturer | Lecturer | LecturerRequest | LecturerRequest | có | A |
| /subjects | Subject | Subject | SubjectRequest | SubjectRequest | có | A |
| /rooms | Room | Room | RoomRequest | RoomRequest | có | A |
| /classes | CourseClass | CourseClass | ClassRequest | ClassRequest | có | GET A/L/S theo quyền lớp; ghi A |

Mã hồ sơ/môn/phòng/lớp duy nhất. UserId của Student/Lecturer duy nhất khi khác null. Email tài khoản duy nhất. DTO không cho gán UserId tùy ý.
Hồ sơ liên kết user không sửa định danh/xóa (409). Student có Enrollment/Attendance không xóa. Lecturer có lớp không xóa. Subject có lớp không xóa. Room có buổi không sửa/xóa. Class có buổi không sửa/xóa; có enrollment cũng không xóa. Foreign key Restrict bảo vệ không xóa dây chuyền lịch sử.

| Method / path | Quyền | Body/query | Data |
|---|---|---|---|
| GET /classes/{classId}/students | A/L sở hữu | query danh sách | Page<Student> |
| POST /classes/{classId}/students | A | EnrollmentRequest | Enrollment (201) |
| DELETE /classes/{classId}/students/{studentId} | A | — | null |
| GET /classes/{classId}/sessions | A/L/S thuộc lớp | query danh sách | Page<Session> |
| POST /classes/{classId}/sessions | A/L sở hữu | SessionRequest | Session (201) |
| GET /sessions/{sessionId} | A/L/S thuộc lớp | — | Session |
| PUT /sessions/{sessionId} | A/L sở hữu | SessionRequest | Session |
| DELETE /sessions/{sessionId} | A/L sở hữu | — | null |
| POST /sessions/{sessionId}/open | A/L sở hữu | — | Session |
| POST /sessions/{sessionId}/close | A/L sở hữu | — | Session |
| GET /sessions/{sessionId}/qr | A/L sở hữu | — | Qr |
| GET /sessions/{sessionId}/attendance | A/L sở hữu | query danh sách | Page<Attendance> |

Enrollment bị khóa thay đổi khi lớp có buổi Open/Closed để giữ danh sách lịch sử. Buổi chỉ sửa/xóa ở Scheduled và chưa có kết quả. Chỉ mở Scheduled trong `start <= serverNow < attendanceCloses`. Đóng Open sinh Absent cho SV chưa có kết quả, vô hiệu QR. Không mở lại Closed.
Worker tự đóng Open quá giờ mỗi 15 giây; đến attendanceCloses thì check-in bị từ chối ngay dù worker chưa chạy. API dừng thì worker xử lý khi chạy lại. Scheduled không tự mở.

## QR và điểm danh

QrPayload là **string chứa JSON**, Flutter dùng string này tạo QR:
```json
{"success":true,"message":"Thành công.","data":{"sessionId":123,"qrPayload":"{\"type\":\"attendance\",\"sessionId\":123,\"token\":\"<random-token>\"}","expiresAtUtc":"2026-10-22T02:02:00Z"},"errors":null}
```
Nội dung QR sau parse: `{"type":"attendance","sessionId":123,"token":"..."}`. Không chứa JWT/password/thông tin sinh viên. Token ngẫu nhiên 32 byte, hash lưu server; bản Data Protection mã hóa dùng lấy lại QR. QR tối đa 2 phút, không vượt closes/end. Lấy lại khi còn hạn trả cùng payload. Một QR dùng cho nhiều SV. Đóng phiên làm mọi QR phiên hết hiệu lực.

| Method / path | Quyền | Body/query | Data |
|---|---|---|---|
| POST /attendance/check-in | S | CheckInRequest | Attendance (201) |
| PUT /attendance/{attendanceId} | A/L sở hữu buổi | AttendanceUpdateRequest | Attendance |
| GET /attendance/{attendanceId}/changes | A/L sở hữu buổi | query danh sách | Page<AttendanceChange> |
| GET /me/classes | A/L/S | query danh sách | Page<CourseClass> |
| GET /me/sessions | A/L/S | query danh sách | Page<Session> |
| GET /me/attendance | S | query danh sách | Page<Attendance> |
| GET /dashboard | A/L/S | — | Dashboard |

StudentId lấy từ JWT và hồ sơ liên kết, không nhận từ body. Check-in yêu cầu SV hoạt động, thuộc lớp; phiên Open và `start <= now < closes`; QR đúng phiên/còn hạn; tọa độ hợp lệ, accuracy 0.01..50 m; Haversine distance <= allowedRadiusMeters. Không cộng accuracy vào bán kính. Tại hoặc trước `start + lateAfterMinutes`: Present; sau ngưỡng và trước đóng: Late. Unique SQL `(ClassSessionId,StudentId)` cùng khóa giao dịch chống gửi trùng, kể cả đồng thời.

```json
{"sessionId":123,"qrToken":"<token-doc-tu-qr>","latitude":10.762622,"longitude":106.660172,"accuracyMeters":10}
```
```json
{"success":true,"message":"Thành công.","data":{"id":1,"sessionId":123,"studentId":1,"status":"Present","checkedInAtUtc":"2026-10-22T02:00:00Z","latitude":10.762622,"longitude":106.660172,"accuracyMeters":10,"distanceMeters":0},"errors":null}
```
QR/GPS sai hoặc ngoài bán kính trả 400; không thuộc lớp 403; phiên đóng/chưa mở/đã điểm danh 409. Nếu GPS kém chính xác thì lấy lại vị trí. QR hết hạn thì Lecturer lấy QR mới. QR+GPS không đảm bảo chặn mọi gian lận.

Điều chỉnh trạng thái bắt buộc reason; log actor, UTC, giá trị trước/sau và reason trong cùng giao dịch. Không có API sửa/xóa log. SV xem lịch sử riêng qua `/me/attendance`. Dashboard Student chỉ đếm kết quả mình, Lecturer đếm lớp được giao, Admin đếm toàn bộ; studentCount/lecturerCount/userCount chỉ có số ở Admin, hai role còn lại null. Absent tính khi đã có bản ghi sau đóng.

## Tài khoản

| Method / path | Quyền | Body/query | Data |
|---|---|---|---|
| GET /users | A | query danh sách | Page<UserAccount> |
| POST /users/lecturer | A | CreateLecturerUserRequest | User (201) |
| PATCH /users/{userId}/active | A | ActiveRequest | Active |

Lecturer phải có hồ sơ chưa liên kết. Email/fullName lấy từ hồ sơ, role cố định Lecturer; tạo user và liên kết trong cùng giao dịch. Email chưa xác minh, login để nhận OTP VerifyEmail rồi theo luồng Auth. Không tự khóa tài khoản đang sử dụng (409). Không trả password/hash/security stamp.

## Tích hợp Flutter

1. `.env`: `BASE_URL=https://<conveyor-host>/api/`; nối endpoint không thêm `/api` lần nữa.
2. Đọc data và data.items với response phân trang; xử lý HTTP status cùng success.
3. Password login → OTP → JWT, lưu token bằng secure storage phù hợp. Mở app gọi auth/me; 401 thì login lại; dashboard theo user.role.
4. Lecturer dựng QR từ qrPayload. Student scan, parse type/sessionId/token, lấy GPS mới, gửi CheckInRequest. Không gửi studentId hay thời gian client.
5. Gia tốc chỉ xử lý trên Flutter, không ảnh hưởng điểm danh, không có API lưu. Không đưa SQL/SMTP/signing secret vào app.
