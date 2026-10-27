# Giao diện theo GitHub 8a12384 — trạng thái đang nghiệm thu

Đã fast-forward từ 61d46e6 tới 8a12384, giữ mã BLL/DAL/DTO/database/tests mới từ Mac.
WIN nối AppServices cho Owner/Homestay/Room/Contract/Task; FileService dùng DataScope.

## Các màn đã nối

- F22 danh sách chủ, tìm/lọc/phân trang/xuất CSV, tạo/sửa Pending, đổi ngân hàng
  qua bước xác minh, duyệt/tạm ngưng/mở lại; tài khoản Kế toán chỉ đọc.
- F15 danh sách homestay và hàng chờ, lọc khu vực/trạng thái, tạo nháp, gửi duyệt.
- F16 thông tin, tiện nghi, ảnh, giấy tờ, loại phòng, phòng, giá, chính sách hủy,
  hợp đồng, audit, khóa phòng và điều kiện duyệt. Nút ghi theo AllowedActions.
  Cập nhật phòng/giá/khóa dùng RowVer homestay như hợp đồng M4.2B.
- F23 hợp đồng, chọn homestay qua lookup, tạo với phần trăm hoa hồng đổi về tỷ lệ,
  gia hạn/chấm dứt. Menu ghi chỉ dành Quản lý.
- F19 giao/sửa/hủy việc, F14 bắt đầu/hoàn thành việc của mình, hạn giờ Việt Nam.
- F18 gán khu vực, đổi chức vụ, nghỉ việc, xem phiên và buộc đăng xuất.
- F05 số liệu Quản lý lấy GetManagerDashboardAsync, ngoại lệ lấy BLL.
- F29 thêm thông tin nhận hoàn theo hợp đồng mới. F30 dùng lookup homestay và
  kỳ hiện tại theo giờ SQL. F32 sửa ánh xạ cột ba nhóm công nợ.

## Kiểm tra hiện có

Build Release toàn solution: 0 lỗi/0 cảnh báo; Rules 1176/1176, 0 Skip.
Hồi quy WIN 53/53. Kiểm ràng buộc UI/role mới 17/17.
Bộ SQL/chụp ảnh mới đang chờ chạy lại sau khi sửa tham chiếu customerNameFilter
đã bị code mới bỏ. Không coi lượt thiếu field là lỗi ghi SQL; chưa tính SQL mới đạt.
Bộ thử dùng ô tìm kiếm tổng hợp hiện tại, còn kiểm lọc tên khách qua SQL.

## Dữ liệu và ảnh báo cáo

Bộ Kiem-tra-va-chup-bao-cao-Administrator.cmd chạy trước kiểm UI rồi tạo database
mới HomestayWinCheck_<random>, dùng dữ liệu seed và tạo thêm chủ/homestay/việc mẫu
qua BLL/GUI. Ảnh ở outputs/anh-bao-cao-SQL-mau là dữ liệu mẫu SQL tạm, không phải
ảnh dữ liệu thật của khách hàng. Sau đó chạy toàn bộ xUnit SQL (ít nhất 225 ca).
Chỉ khi đạt mới áp dụng outputs/Cap-nhat-view-quyen-D12.sql lên HomestayDesktop_Dev,
đúng quyền người dùng đã duyệt: sửa view vw_Payables và quyền DELETE SettlementItems;
không seed/reset/mật khẩu/DML dữ liệu chính. Script đặt trong transaction.
Bộ thử luôn dọn đúng database ngẫu nhiên của lượt đó, không xóa database có sẵn.

Chưa đánh dấu hoàn tất W4/W5/W6 khi thiếu nghiệm thu SQL/DPI/Designer.
F06 khóa trực tiếp trên lịch, giao nhiều đơn/ngoại lệ đầy đủ, xử lý sửa metadata ảnh,
ảnh gắn từng loại phòng và các phần W7 vẫn cần lượt hoàn thiện/kiểm tiếp.
Chưa commit/push thay đổi WIN của lượt mới này.

## Nghiệm thu đã nhận

Người dùng đã chạy: 225/225 xUnit SQL, 0 Skip; đủ các màn mới đọc SQL,
tạo chủ và nháp homestay qua GUI, hủy/duyệt/chi hoàn qua GUI đều qua.
Lượt chạy dừng sau đó do tệp cập nhật bị dính GOCREATE (lỗi ghép tệp của WIN).
Đã sửa xuống dòng, người dùng chạy riêng updater: REPORT-LATEST-UPDATE.txt RESULT PASS,
không reset dữ liệu hay đổi mật khẩu. Giữ cả hai log và ghi rõ thứ tự giải quyết.
13 ảnh SQL mẫu đã xem kiểm, gói outputs/Anh-bao-cao-giao-dien-moi.zip.
Bản mới đã mở màn đăng nhập từ bin/ReportLatest. Các sửa WIN này chưa commit/push.