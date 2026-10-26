# Tiến độ WIN — 27/10/2026

## Cách chạy và xem bố cục

Mở HomestayDesktop.sln bằng Visual Studio Community 2026, chọn Homestay.GUI làm Startup Project.
Ở danh sách hồ sơ cạnh nút Run, chọn **Xem bố cục (không dữ liệu)** rồi F5.
Hoặc chạy ứng dụng bình thường để mở F04, sau đó bấm **Xem bố cục**.

Chế độ xem bố cục không đăng nhập, không tạo phiên, không kết nối SQL và không lưu nghiệp vụ.
Ô chọn vai trò chỉ đổi cách xem bản thiết kế; không phải cơ chế phân quyền.
Các vùng/mục/nút được trình bày để rà soát thiết kế. Chưa nghiệm thu quyền và trạng thái.
Chế độ chạy bình thường kiểm tra cấu hình/schema trước khi cho đăng nhập; hiện dừng an toàn
vì chưa có dịch vụ BLL thực hiện kiểm tra kết nối và xác thực.

## Theo WORK_PLAN.md

| Mục | Kết quả thực tế |
|---|---|
| W1.0 | VS2026 18.10.3, desktop workload, .NET SDK 10.0.401 đã có. Hai instance SQLEXPRESS/SQLEXPRESS01 có đăng ký, nhưng thử kết nối chỉ đọc đều timeout. Chưa chạy bộ cài SQL, chưa kiểm collation, tài khoản SQL hay dữ liệu mẫu. |
| W1.1 | Phần WIN đã build/chạy Form1 và được người dùng push ở bdbda24. Chưa có xác nhận build từ máy Mac nên chưa đánh dấu x cho toàn bộ mốc. Form1 đã được thay bằng F01–F04 trong phần việc tiếp theo. |
| W1.2 | Đã có AppServices, JSON settings, bắt lỗi, FileLogger, F04 và DPAPI LocalMachine. Có kiểm tập SchemaVersions, chặn lưu trước kiểm tra. Chưa kết nối dịch vụ BLL nên chưa kiểm SQL thật; mục này chưa hoàn thành. |
| W1.3 | Hoàn thành control GUI, dùng DTO từ 455d485: PageRequest, PagedResult và Lookup. Kiểm thử các cỡ trang, tìm không dấu, định dạng, CSV và bỏ tải cũ đạt. |
| W1.4 | Đã có bố cục F01/F02/F03, tab không mở trùng, thanh trạng thái, Ctrl+K/Ctrl+N, thông báo, xác nhận thoát. Chưa có Authorizer/AuthService/SessionService; chưa đăng nhập, đổi mật khẩu hay khóa màn hình thật. |
| W1.5 | Bố cục F18 và F24 đã có. Nút thao tác chờ StaffService/CatalogService. |
| W2.1–W2.6 | Bố cục F05–F10/F13 đã có, gồm báo giá từng đêm và lịch. Chưa có dữ liệu, báo giá, đồng hồ giữ phòng, tạo đơn, thu cọc hay thông báo thật. |
| W3.1–W3.4 | Bố cục F11/F20/F27/F29 và các vùng/nút vòng đời đơn đã có. Chỉ loại Hủy và tab Hoàn tiền. Chưa nối dịch vụ; chưa có thao tác tiền. |
| W4.1–W4.5 | Bố cục F15/F16/F22/F23 và vùng khóa phòng đã có. Chưa nối dịch vụ. |
| W5.1–W5.3 | Vùng tổng quan QL, ngoại lệ, phiên, phạm vi, công việc đã có. Chưa nối dịch vụ. |
| W6.1–W6.4 | Bố cục F28/F30/F31/F32 và vùng tổng quan KT đã có. F28 chỉ nhập tay, không có nhập tệp. Chưa nối dịch vụ. |
| W6.5 | Common/DocumentPrinter dùng PrintDocument, lặp tiêu đề/cột và phân trang. Đã kiểm 60 dòng giả lập riêng. Chưa có DTO bản in/số tiền bằng chữ nên các nút In vẫn chờ; chưa nghiệm thu phiếu thu/chi và đối soát thật. |
| W7.1–W7.2 | Bố cục báo cáo, nhật ký, lịch sử cá nhân và hiệu suất đã có. Chưa có dữ liệu/chỉ tiêu thật. |
| W7.3 | Có vùng cuộn, nút ?, một số phím tắt và lưu lựa chọn cột qua SetPreferenceIdentity. Chưa gắn định danh phiên; chưa kiểm đầy đủ 100/125/150% trên 1366×768; chưa khóa sau 30 phút. |
| W8.1–W8.2 | Chưa có ảnh luồng nghiệp vụ trên seed, ảnh cây solution trong VS hay kiểm tay phân quyền. Ảnh layout-preview chỉ là ảnh kết xuất bố cục trống; không thay ảnh nghiệm thu. |

Có 29 form thuộc phạm vi hiện tại, mỗi form có .cs và .Designer.cs riêng, dùng TableLayoutPanel/Dock/Anchor
và control chuẩn WinForms. F12/F21 tạm chưa làm, F17 bỏ. Không sửa các thư mục do MAC phụ trách,
không sửa solution hoặc .csproj khác GUI, không thêm NuGet.
Các cửa sổ soạn/sửa nghiệp vụ chi tiết sẽ cần hoàn thiện khi có hợp đồng ghi; bố cục hiện tại là nền để tiếp tục.

## Điểm nối để tiếp tục sau hợp đồng MAC

- AppServices.ReadSchemaVersionsAsync: gắn với điểm khởi tạo BLL; trả tập phiên bản sau khi kiểm kết nối.
  GUI không tham chiếu DAL trực tiếp và không viết SQL.
- F01/F02: dùng LoginRequest/SessionInfo khi có AuthService và SessionService. Không lưu mật khẩu ứng dụng.
- ScreenRegistry: một danh sách form trung tâm. EnabledProductionForms hiện rỗng;
  chỉ bật mục đã tích hợp và dùng Authorizer.Can từ BLL khi có M1.3/M1.4.
- PagedGrid.QueryRequested cung cấp PageRequest; SetPage<T> nhận PagedResult<T> và hàm ánh xạ ô.
  SortColumn là tên logic, BLL phải đối chiếu danh sách cho phép, không ghép trực tiếp vào SQL.
- PagedGrid.ExportFilteredAsync phải trả toàn bộ kết quả lọc từ service, không chỉ trang đang xem.
  Khi có dòng được chọn, xuất đúng các dòng đã chọn; luôn có BOM, escape CSV và chặn công thức Excel.
- SearchPicker.SetItems nhận Lookup. Không dấu được chuẩn hóa trong UiFormat.
- AsyncLoader chỉ áp dụng kết quả mới nhất, kể cả service không tuân cancellation.
- PagedGrid.SetPreferenceIdentity gọi với UserId và mã lưới sau đăng nhập để lưu cột theo từng người.
- DocumentPrinter nhận dữ liệu trình bày; BLL cung cấp tổng và số tiền bằng chữ.
- Tất cả nút chưa nối dịch vụ hiện báo **Chức năng đang làm**, không giả lập ghi thành công.

## Kiểm tra đã chạy

Build solution Debug và Release bằng .NET trên Windows; không lỗi/cảnh báo ở mã sản phẩm.
Kiểm tra tự động GUI riêng (không sửa tests/ của MAC): tìm không dấu, tiền, ngày Việt Nam;
kết quả tải cũ/dispose; BOM/quote/newline và công thức CSV; DPAPI round-trip và không chứa mật khẩu rõ;
giữ log 30 ngày và không ghi nội dung exception có bí mật; schema đúng/thiếu/mới;
PrintDocument nhiều trang; mở 29 form ở DPI 144; tab không trùng; bỏ tab khi đổi vai trò xem;
Lookup và phân trang 10/20/50/100 theo DTO.
Chi tiết kết quả nằm ở docs/screenshots/layout-preview/WIN_CHECKS.txt.

Chưa thực hiện: SQL end-to-end, phân quyền BLL, Mac build, mở từng form trong VS Designer,
các mức DPI khác/màn nhỏ đầy đủ, in trên máy in thật, và ảnh báo cáo trên dữ liệu mẫu.
