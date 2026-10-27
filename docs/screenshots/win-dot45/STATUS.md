# WIN đợt 4–5 — đã nối và kiểm tra luồng chính

Nền GitHub `6c0d1bc`; thực hiện theo `docs/HUONG_DAN_WIN_NOI_FORM_DOT4_5.md` và notes M4.1, M4.2A/B, M5.1. Giữ các sửa đổi WIN có trước. Không sửa DTO/BLL/DAL, database hoặc tests của MAC.

## Kết quả cuối ngày 28/10/2026

| Kiểm tra | Kết quả |
|---|---|
| Build solution và bộ thử WIN | 0 lỗi, 0 cảnh báo |
| Rules mới nhất | 1230/1230, không skip |
| Hồi quy WIN | 53/53 |
| Kiểm UI/menu/phiên bản | 37/37 |
| Luồng GUI/BLL trên SQL thật | 94 bước PASS |
| Toàn bộ test BLL trên database tạm | 231/231, 0 lỗi, 0 skip |
| Kết quả tổng | RESULT PASS |
| Dọn database tạm | Cleanup PASS |
| Database chính | Không thay đổi |

Bằng chứng: `WIN-DOT45-SQL.txt`, `WIN-DOT45-UI.txt`, `WIN-DOT45-REGRESSION.txt`.
Tổng hai assembly MAC: 1230 Rules + 231 BLL = 1461 ca đạt. Không dùng kết quả bản 225 SQL trước để xác nhận bản này.

## Nội dung WIN

- AppServices lắp đủ 5 service mới, FileService nhận scope; DAL chỉ dùng ở composition root.
- F22: tạo/sửa chủ, giữ dữ liệu nhạy cảm cũ bằng null, đổi ngân hàng sau xác minh, duyệt/ngừng/mở lại. Chuyển F22 → F15 giữ mã/tên chủ vừa tạo hoặc đang chọn. Chủ Approved chưa có homestay có thể mất khỏi lookup NV; biểu mẫu tạo giữ ID đã nhận, BLL vẫn kiểm tồn tại/phạm vi khi ghi, không mở rộng truy vấn đọc hoặc chia sẻ cache giữa các phiên.
- F15: danh sách/chờ duyệt, lọc khu vực/chủ/trạng thái qua SQL; tạo nháp mở hồ sơ; gửi duyệt theo AllowedActions.
- F16: thông tin, tiện nghi, ảnh (loại phòng/chú thích/thứ tự/bìa), giấy tờ, loại phòng/phòng, ba mức giá, chính sách, hợp đồng, lịch sử, checklist. Khóa nút trước await; mọi ghi con dùng RowVer homestay và tải lại hồ sơ. Khóa sửa giá đã hiệu lực/loại của phòng đang sửa; ẩn tab theo cờ quyền; Reopen đúng hành động, thông báo QL khác duyệt/D11.
- F23: tạo/gia hạn/chấm dứt, đổi phần trăm hoa hồng sang tỷ lệ. ActivateAsync được gọi bằng nút “Kiểm tra lại hiệu lực” theo notes: chỉ revalidate Active, không hồi sinh Terminated.
- F06: lịch từ Rooms.GetCalendarAsync; QL chọn ô để khóa khoảng/mở toàn bộ khoảng khóa; không tự tính chiếm phòng. Giờ khởi tạo từ SQL; lấy quyền/token parent trước ghi và nạp lại sau ghi.
- F29: lưu tài khoản nhận hoàn rồi tải lại phiếu để lấy token mới trước chi; khóa cả nút sửa tài khoản khi tải.
- F05/F08: thẻ QL mở đúng hàng đợi; bốn loại ngoại lệ trên grid riêng, phân trang/xuất theo SQL. Giao nhiều khử trùng ID, giữ mã đơn và copy token, từ chối các phiên bản không khớp, lấy giao người nhận hợp lệ cho tất cả đơn, gọi AssignManyAsync một lần. Mở F19 kèm BookingId.
- F18: danh sách theo khu vực, gán phạm vi; toàn hệ thống chỉ QL, KT không gán khu vực; phiên mở mọi nhân viên hoặc người chọn, lịch sử người chọn, buộc đăng xuất có lý do/chặn tự kết thúc; đổi chức vụ/tạm nghỉ/nghỉ việc kèm cảnh báo tác động.
- F19/F14: tạo/sửa/hủy việc, người nhận theo liên kết từ BLL; bắt đầu, hoàn thành bắt buộc kết quả; cờ quá hạn tô đỏ. DueAtVietnam dùng Unspecified đúng DTO; BLL chuyển UTC. Sau ghi tải lại, người dùng chọn lại dòng để thao tác tiếp với token mới.

## Luồng được xác nhận trên SQL

Đưa homestay vào bán: NV tạo chủ → QL duyệt → NV tạo draft bằng mã chủ đã biết → thử duyệt khi thiếu và đối chiếu nguyên danh sách thiếu → chuẩn bị tiện nghi/ảnh/pháp lý/loại phòng/phòng/ba giá/hợp đồng → gửi duyệt → nút F16 duyệt thật → tra được phòng và tạo được đơn mới. Phần chuẩn bị hồ sơ trong bộ thử dùng cả BLL và GUI; không khẳng định từng nút tải tệp đã được bấm bằng GUI.

Nút F06 khóa/mở thực sự đổi lịch; F08 giao hai đơn; F19 tạo việc gắn đúng đơn; F14 bắt đầu/hoàn thành có kết quả; F18 buộc đăng xuất phiên khác, đọc lịch sử đúng người, đọc danh sách theo khu vực. Các luồng đợt 1–3 hồi quy cũng đạt.

## Ảnh và cách mở

20 ảnh thật trên dữ liệu SQL mẫu ở `sql-demo/`, kèm `sql-demo/HUONG_DAN_ANH.md`. Ảnh F06, F08, F16 checklist và F18 lịch sử đã được xem kiểm tra. F30 kỳ hiện tại không có dòng; không dùng ảnh trống để chứng minh đã lập đối soát.

Trên máy WIN hiện tại: `outputs/Mo-giao-dien-DOT45.cmd` hoặc `Mo-giao-dien-moi-nhat.cmd` mở đúng bản đã thử, dùng connection.dat cục bộ đã mã hóa. Profile/mật khẩu/database không đưa vào Git.

## Giới hạn và lịch sử thử

Đã hoàn thành nối các mục W4/W5 và kiểm luồng chính; chưa tuyên bố mọi nút/ngoại lệ đều được kiểm tay, DPI 100/125/150% hay toàn bộ VS Designer. Theo hướng dẫn, không phát triển F25/F26/Hiệu suất hoặc tính năng đợt 7–8. Các chỉnh sửa Designer/resx từ người dùng được giữ; khởi tạo Container cho F24/F23 để tránh CS0649.

Các lượt trước có lỗi ở bộ thử: token Windows thường; dùng chung khách vượt 3 đơn chờ; bấm trước màn con sẵn sàng; không chọn lại việc sau reload. Đã sửa launcher và dữ liệu/thao tác thử, giữ nguyên giới hạn nghiệp vụ. Log BEFORE-TASK-READY-FIX là lịch sử thất bại, không phải kết quả cuối. Lượt cuối v4 đạt đầy đủ và dọn đúng database tạm.

Gói bàn giao gồm mã WIN, bộ kiểm tra, kết quả SQL và ảnh dữ liệu mẫu.
