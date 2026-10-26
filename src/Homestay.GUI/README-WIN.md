# Tiến độ WIN — 27/10/2026

Mở HomestayDesktop.sln bằng Visual Studio Community 2026, chọn Homestay.GUI làm Startup Project.
Hồ sơ **Xem bố cục (không dữ liệu)** hoặc tham số --preview mở bản xem 29 màn hình.
Chế độ này không tạo phiên, không kết nối SQL và không ghi nghiệp vụ.

## Đợt 1 sau hợp đồng M1.4

Đã ghép các dịch vụ BLL M1.4 vào AppServices theo docs/M1.4_SERVICES_NOTES.md.
GUI chỉ tham chiếu BLL/DTO; AppServices là composition root tạo các đối tượng hạ tầng/DAL.
Không có SqlConnection, câu SQL, command hay lời gọi repository trong GUI. Không thêm NuGet.

| Mục | Kết quả thực tế |
|---|---|
| W1.0 | Có VS2026 18.10.3, workload desktop, SDK10.0.401. SQL Express trên Windows kết nối TCP 127.0.0.1,51433; database HomestayDesktop_Dev có 38 bảng, 7 tài khoản, 24 đơn, Vietnamese_CI_AS. Đọc tiếng Việt đúng, tài khoản SQL ứng dụng không sysadmin/db_owner. SQL Server đang cài là 2025, không đúng bản 2022 ghi trong kế hoạch; chưa nghiệm thu đầy đủ tương thích. |
| W1.1 | Project GUI đã được người dùng push, build được trên Windows. Chưa có xác nhận riêng cho phiên bản mới này từ VS Designer và máy Mac. |
| W1.2 | Có config JSON, DPAPI LocalMachine, kiểm đúng tập SchemaVersions 1,2,3,4,10,20, bắt lỗi và log. Đã nối StartupService từ 77be8d0 bằng factory chung, SQL thật trả đúng phiên bản; check → lưu/đọc DPAPI → initialize đạt. Ca SQL StartupServiceTests của MAC đạt. Chưa kiểm tay nút F04. |
| W1.3 | Control dùng chung hoàn thành. PagedGrid có khóa sắp xếp logic cho service, bỏ cột không hỗ trợ. |
| W1.4 | Đã nối F01/AuthService, F02/SessionService, F03/Authorizer.Can. Bắt buộc đổi mật khẩu tạm trước khi mở chính; hủy đổi thì logout. Heartbeat 60 giây, khóa sau Session.IdleMinutes, mở khóa, logout, hết phiên về đăng nhập. Chỉ bật F02/F18/F24; các màn chưa nối ẩn tập trung ở ScreenRegistry. Bộ thử dịch vụ ba vai trò đã đạt trên DB tạm (RESULT PASS, Cleanup PASS). Giao diện Quản lý, menu, tái sử dụng tab và đăng xuất về F01 đã kiểm đạt; còn phiên GUI Nhân viên/Kế toán và các ca ngoại lệ. |
| W1.5 | F18 nối tìm kiếm/phân trang/sắp xếp qua StaffService, xem hồ sơ, tạo NV, khóa/mở, reset và hiện mật khẩu tạm một lần. Xuất toàn bộ bộ lọc qua các trang service; không xuất mật khẩu. F24 nối 6 tab CatalogService, editor khu vực/danh mục/ngày lễ/chính sách/tham số. Gửi giá trị gốc/RowVer để BLL kiểm đồng thời, reload khi xung đột; không tự gửi lại thao tác ghi. Dịch vụ F18/F24 đã đạt trên SQL thật ở DB tạm; giao diện Quản lý đã kiểm đọc/tìm nhân viên và đủ sáu tab danh mục. Chưa nghiệm thu thao tác ghi bằng GUI. |
| W2–W6 | Có bố cục. Đã tải M2.1–M2.4, gồm cài đặt tạo đơn và thu cọc; chưa nối các màn đợt 2. DB chính chưa có cột TransferredAt. |
| W7.3 | Có help/phím tắt, khóa theo idle, lưu cột theo UserId F18/F24. Chưa kiểm đủ DPI100/125/150%, màn1366×768, mở mọi form trong VS Designer. |
| W8 | Chưa có ảnh luồng nghiệp vụ trên dữ liệu mẫu, không dùng ảnh layout-preview thay nghiệm thu. |

Chưa đánh dấu x W1.2/W1.4/W1.5 khi còn kiểm tay giao diện và các luồng thật.
W1.3 vẫn là mốc đã đánh dấu hoàn thành. Phần việc MAC không bị sửa.

## Dùng cấu hình SQL đã có

Program nhận --connection-file "đường dẫn tuyệt đối tới connection.dat".
Tệp vẫn được giải mã DPAPI LocalMachine trên chính máy Windows; không đưa tệp/mật khẩu vào Git.
Mặc định tệp nằm cạnh ứng dụng. Chỉ chạy --configure bằng quản trị Windows để sửa cấu hình đã có.
F04 phải kiểm kết nối và schema thành công trước khi lưu.

Không cần chạy lại bộ cài SQL. Startup bình thường đã đọc được SQL thật bằng StartupService;
không dùng tệp kết quả cài đặt thay kiểm SchemaVersions. Dùng launcher cục bộ Mo-ung-dung.cmd
trong outputs/win-startup để mở cùng cấu hình SQL đang có.

## Kiểm tra

- Build Release cả solution trên Windows: 0 lỗi, 0 cảnh báo.
- Rules từ MAC sau cập nhật 31faa95: 274 đạt.
- Bộ kiểm tra GUI riêng ngoài tests/: 74 đạt, bao gồm bộ cũ, LoginFlow/đổi mật khẩu bắt buộc,
  Authorizer menu 3 vai trò, chặn màn chưa tích hợp, bộ đếm idle, khóa sắp xếp logic,
  editor ngày/vai trò và xóa mật khẩu tạm khi đóng.
- Mở và kết xuất 29 form preview ở DPI144.
- Kiểm SQL độc lập chỉ SELECT: kết nối TCP, số bảng/tài khoản/đơn, collation, Unicode, quyền SQL.
- Kết quả: docs/screenshots/layout-preview/WIN_CHECKS_W14.txt.

Kiểm tra giả lập luồng tài khoản không thay thế đăng nhập thực tế. Chưa chạy các test BLL ghi vào database
của người dùng, chưa kiểm khóa tài khoản sau 5 lần sai, tạo nhân viên thật hoặc đổi tham số thật trên máy WIN.

## Cập nhật StartupService 77be8d0

Đã nối F04/khởi động bằng một CreateFactory duy nhất. Đã chạy ca SQL chỉ đọc của MAC: 1 đạt,
đúng 1,2,3,4,10,20; lỗi database thiếu được dịch đúng; kiểm rồi lưu/đọc DPAPI và initialize đạt.
Kết quả: docs/screenshots/layout-preview/WIN_STARTUP_SQL.txt.
Bộ thử ghi đã chạy đạt trên database tạm riêng, có RESULT PASS và Cleanup PASS; chi tiết tại docs/screenshots/layout-preview/WIN_DOT1_SQL_ACCEPTANCE.txt.

## Cập nhật nghiệm thu dịch vụ SQL — 27/10/2026

- Đã rebase commit WIN lên origin/dev 31faa95 (M2.3/M2.4); commit F04 sau rebase là bed2ce1.
- Build Release solution: 0 lỗi, 0 cảnh báo; Rules 274/274 đạt.
- Bộ GUI riêng: 74 kiểm tra đạt, gồm 29 form preview; không thay thế nghiệm thu thao tác thật.
- Bộ thử dịch vụ do người dùng chạy Administrator: 16 PASS, RESULT PASS, Cleanup PASS. Bao phủ cấu hình, ba vai trò, đổi mật khẩu, heartbeat/mở khóa, F18, F24 và logout trên DB tạm.
- Kiểm tra chỉ đọc sau bộ thử: HomestayDesktop_Dev có 38 bảng, 7 tài khoản, 24 đơn; Unicode/collation đúng, login ứng dụng không sysadmin/db_owner.
- DB chính thiếu Payments.TransferredAt của D10. Chưa sửa schema, chạy lại seed hay reset DB chính. Cần phương án cập nhật bảo toàn dữ liệu trước nghiệm thu W2 trên DB này.
- Đã kiểm giao diện thật bằng phiên Quản lý: F18 7 nhân viên, lọc NV0002 còn 1 dòng rồi khôi phục 7; đủ 6 tab F24 (3/8/5/13/2/26 dòng), mở lại không trùng tab, đăng xuất về F01. Không ghi thay đổi nhân viên/danh mục/tham số trên DB chính. Chi tiết: docs/screenshots/layout-preview/WIN_DOT1_UI_ACCEPTANCE.txt. Còn F04 thao tác tay, phiên GUI NV/KT và các ca ngoại lệ; chưa đánh dấu hoàn thành toàn bộ W1.2/W1.4/W1.5.
