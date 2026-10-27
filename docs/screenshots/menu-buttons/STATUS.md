# Menu bên trái dạng nút — 28/10/2026

Thay ListBox chữ trơn bằng FlowLayoutPanel cuộn và 28 Button khai báo tĩnh trong FrmMain.Designer.cs. Chỉ hiện/bật các nút có trong ScreenRegistry.Production cho phiên hiện tại; giữ thứ tự và kiểm quyền khi mở màn. Nút có viền, khoảng cách, trạng thái rê chuột/nhấn, Tab focus, tooltip và tên truy cập. Không dùng ký tự & làm phím tắt nên tên hiển thị đầy đủ.

Tab đang mở tô xanh nút tương ứng; chi tiết đơn/thu/hủy gắn Danh sách đơn, hồ sơ gắn Danh sách homestay, chi tiết đối soát gắn Danh sách đối soát. Bấm lại tái sử dụng tab. Sidebar tự điều chỉnh kích thước theo chiều rộng/DPI và cuộn khi nhiều chức năng.

Build Release: 0 lỗi/0 cảnh báo. MenuButtonsChecks đạt 20/20: ba vai trò, nút đúng quyền, focus/viền, mở và tái sử dụng tab, màu tab đang chọn, hồ sơ mở trực tiếp và resize 1320/900. Đây là kiểm giao diện không SQL, không chạy lại các luồng ghi/database vì thay đổi chỉ nằm ở menu. Ảnh Menu_QuanLy.png là xem trước bố cục, không phải ảnh dữ liệu SQL.

Máy hiện tại dùng outputs/Mo-giao-dien-menu-moi.cmd hoặc Mo-giao-dien-moi-nhat.cmd. Cấu hình connection.dat được tái sử dụng cục bộ; không đưa vào Git. Không sửa các thay đổi FrmDashboard.resx do người dùng tạo.

Gói cập nhật gồm menu dạng nút trong Designer, xử lý chọn màn, 20 kiểm tra và ảnh xem trước.
