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
| W1.4 | Đã nối F01/AuthService, F02/SessionService, F03/Authorizer.Can. Bắt buộc đổi mật khẩu tạm trước khi mở chính; hủy đổi thì logout. Heartbeat 60 giây, khóa sau Session.IdleMinutes, mở khóa, logout, hết phiên về đăng nhập. Đợt 1 đã kiểm F02/F18/F24; phạm vi đợt 2 hiện được bật tập trung ở ScreenRegistry. Bộ thử dịch vụ ba vai trò đã đạt trên DB tạm (RESULT PASS, Cleanup PASS). Giao diện Quản lý, menu, tái sử dụng tab và đăng xuất về F01 đã kiểm đạt; Nhân viên/Kế toán đã kiểm đúng menu, vai trò/khu vực và đăng xuất về F01; còn các ca ngoại lệ. |
| W1.5 | F18 nối tìm kiếm/phân trang/sắp xếp qua StaffService, xem hồ sơ, tạo NV, khóa/mở, reset và hiện mật khẩu tạm một lần. Xuất toàn bộ bộ lọc qua các trang service; không xuất mật khẩu. F24 nối 6 tab CatalogService, editor khu vực/danh mục/ngày lễ/chính sách/tham số. Gửi giá trị gốc/RowVer để BLL kiểm đồng thời, reload khi xung đột; không tự gửi lại thao tác ghi. Dịch vụ F18/F24 đã đạt trên SQL thật ở DB tạm; giao diện Quản lý đã kiểm đọc/tìm nhân viên và đủ sáu tab danh mục. Chưa nghiệm thu thao tác ghi bằng GUI. |
| W2–W6 | Đợt 2 đã nối F05/F06/F07/F08/F09/F10/F13 và F03 tìm nhanh/thông báo/job, SQL 37 PASS. Đợt 3–6 còn bố cục. Xem giới hạn bên dưới. |
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
- Đã kiểm giao diện thật bằng phiên Quản lý: F18 7 nhân viên, lọc NV0002 còn 1 dòng rồi khôi phục 7; đủ 6 tab F24 (3/8/5/13/2/26 dòng), mở lại không trùng tab, đăng xuất về F01. Không ghi thay đổi nhân viên/danh mục/tham số trên DB chính. Chi tiết: docs/screenshots/layout-preview/WIN_DOT1_UI_ACCEPTANCE.txt. Đã kiểm thêm menu, vai trò/khu vực và logout bằng GUI NV/KT. Còn F04 thao tác tay và các ca ngoại lệ; chưa đánh dấu hoàn thành toàn bộ W1.2/W1.4/W1.5.

## Đợt 2 — bản tích hợp WIN đã kiểm SQL thật

Đã nối dịch vụ thật cho F05 Tổng quan, F06 Tra phòng/lịch, F13 Khách hàng,
F07 Báo giá/tạo đơn, F08 Danh sách đơn, F09 Chi tiết đơn và F10 Thu cọc.
F03 có tìm nhanh Ctrl+K, thông báo theo tài khoản và JobRunner hết hạn giữ phòng
sau đăng nhập/mỗi 60 giây, ngăn tick chồng nhau và hủy khi đóng phiên.

- F06: bộ lọc khu vực/ngày/khách/giá/danh mục/tiện nghi, giá từng đêm,
  chính sách hủy, lịch 14/31 ngày với màu và tooltip, mở chi tiết từ ô có đơn.
- F13: tìm/phân trang/sắp xếp server, tạo/sửa, đổi số khi có quyền, lịch sử;
  danh sách luôn che giấy tờ; ô giấy tờ mới trống giữ nguyên số cũ.
- F07: tìm khách qua SĐT hoặc tạo nhanh trong transaction đặt phòng, chọn
  loại/phòng, báo giá thật, giữ phòng và hướng dẫn chuyển khoản từ kết quả BLL;
  đếm ngược dựa trên thời gian SQL + Stopwatch, không dùng giờ máy để quyết định lưu.
- F08/F09: hàng đợi, tra chính xác, trạng thái đơn/thanh toán riêng,
  RowVer, quyền thao tác từ AllowedActions, nhận phụ trách, hủy chưa có tiền,
  lịch sử và chứng từ. Nút vòng đời đợt 3 tiếp tục ẩn.
- F10: chuyển giờ Việt Nam sang UTC, tiền nguyên, chứng từ JPG/PNG tối đa 5 MB,
  lưu phiếu qua PaymentService, chờ kế toán xác minh, không tự xác minh.
- Kế toán được xem đơn/khách; không mở tra phòng/tạo đơn/thu cọc.
- Danh sách nhân viên để giao lại hiện dùng StaffService cho Quản lý.
  Nhân viên chưa có API lookup người nhận được phép: nút giao lại của NV
  tiếp tục ẩn, cần MAC bổ sung hợp đồng; không yêu cầu nhập Id kỹ thuật.
- Không thêm NuGet, tham chiếu GUI -> DAL, SQL trong form; không sửa thư mục MAC.
- Có bộ thử riêng ngoài repo tại work/w2-flow-checks: tạo/dọn DB tạm,
  đọc các màn trên SQL và bấm Save F07/F10 với phản hồi thành công được ghi nhận
  bởi subclass kiểm thử. Sau tất cả PASS mới chạy cập nhật cấu trúc được người dùng duyệt.
- Người dùng đã duyệt thêm Payments.TransferredAt và CHECK từ D10 vào
  HomestayDesktop_Dev; script cục bộ Cap-nhat-TransferredAt.sql chỉ thêm cấu trúc,
  không đổi dữ liệu cũ/mật khẩu/seed. Script đã chạy thành công sau khi bộ thử đạt; số bảng/tài khoản/đơn/phiếu không đổi.
- Build solution riêng trong bin/W2Verified: 0 lỗi, 0 cảnh báo; Rules 274/274 đạt.
  Bộ kiểm GUI/preview trước sửa cleanup: 74/74 đạt; ca hồi quy cleanup F07 sau sửa đạt.
- W2-FLOW-RESULT.txt: 37 PASS, RESULT PASS, Cleanup PASS. Bằng chứng: docs/screenshots/layout-preview/WIN_DOT2_SQL_ACCEPTANCE.txt. Chưa đánh dấu hoàn thành hàng loạt vì còn lookup giao lại cho NV và các ca kiểm tay.
- Bản mới ở work/w2-app đã được mở sau khi bộ thử SQL và cập nhật cột đạt; đang chờ đăng nhập NV0002 để kiểm menu thực tế.

### Sửa lỗi đóng F07 sau khi tạo đơn

Lượt SQL đầu đã đi qua lưu đơn F07 rồi dừng ở ObjectDisposedException khi đóng form.
Đã tái hiện độc lập: Close giải phóng form, sau đó using/host gọi Dispose lần nữa;
callback Disposed hủy CancellationTokenSource đã được giải phóng. Sửa cleanup có cờ
chỉ chạy một lần. Kiểm tra Close rồi Dispose nhiều lần và Dispose khi chưa Show đều đạt.
Lượt thất bại có Cleanup PASS; chưa chạy cập nhật cấu trúc DB chính.
Đã chạy lại bản sửa: 37 PASS, RESULT PASS, Cleanup PASS; luồng đã qua F10 và cập nhật cột.

### Kiểm giao diện thật W2

Đã kiểm bằng phiên NV0002: đủ menu đợt 2, tổng quan có số liệu, tra 8 loại phòng,
lịch Ba Đình 6 phòng/14 ngày có màu, 15 khách có che CCCD, 16 đơn của NV,
chi tiết đơn hoàn thành có nút ghi bị khóa, phiếu thu và lịch sử đọc đúng.
Màn F07 báo giá thực tế 690.000 đồng/đêm, cọc 207.000 đồng, không lưu trên DB chính.
Thông báo hiện dữ liệu theo người dùng, không đánh dấu đã đọc trong lượt kiểm tay.
Đã phát hiện/sửa Enter ở tìm nhanh, thêm ca hồi quy bàn phím; kiểm lại native
mở đúng đơn từ kết quả đạt. Ctrl+N đã nối Tạo đơn trong chế độ sản xuất theo quyền.
Bằng chứng: WIN_DOT2_UI_ACCEPTANCE.txt và WIN_DOT2_REGRESSION.txt cùng thư mục bằng chứng SQL.
Bản đang chạy là work/w2-app-final; launcher Mo-ban-W2.cmd đã trỏ tới bản này.
Chưa có ảnh W8 hoặc kiểm toàn bộ DPI/Designer. NV giao lại đơn vẫn chờ lookup từ MAC.

## Rà soát logic ngày 27/10/2026

Đã sửa trạng thái nút sau thao tác, báo giá và tìm khách trả về muộn, đóng form
khi đang lưu, xuất CSV giữ sắp xếp, đổi SĐT khách chưa có đơn cho Nhân viên,
F2/Ctrl+F và tìm nhanh chống mở lặp. F09 chỉ bật nút sau khi tải được AllowedActions.
Build solution 0 lỗi/0 cảnh báo; Rules 274/274; GUI 74/74; hồi quy mới 18/18;
SQL WIN trên DB tạm 36 PASS + RESULT PASS + Cleanup PASS (không sửa DB chính).
80 ca SQL xUnit của MAC chưa được chạy trong lượt này vì thiếu biến kết nối DB thử.
WORK_PLAN đã cập nhật trạng thái form và các ô WIN có bằng chứng.
Báo cáo đầy đủ: docs/screenshots/logic-audit/RA_SOAT_WORK_PLAN_2026-10-27.md.
Bộ hồi quy có thể chạy lại: docs/screenshots/logic-audit/checks/GuiAuditChecks.csproj.
Đợt 3–7 vẫn thiếu hợp đồng/dịch vụ, giao diện chưa tích hợp tiếp tục ẩn.
Launcher cục bộ Mo-ban-W2.cmd và Mo-ban-da-ra-soat.cmd hiện trỏ tới bản đã rà soát
trong bin/AuditSolution/bin/Homestay.GUI/release, chỉ mở sau SQL RESULT PASS và Cleanup PASS.
