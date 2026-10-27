# F09: khóa nút khi đang tải, sửa bộ kiểm M33B

Lượt người dùng chạy có 67 bước WIN PASS, đã qua gửi/duyệt/từ chối hủy và ghi chi
hoàn thực tế trên database tạm; dừng ở UI timeout của hủy homestay không phục vụ.
Có Cleanup PASS. Chưa đến xUnit SQL nên không tính 131 ca SQL là đã chạy.

Đã tái hiện trước sửa: constructor F09 chỉ ẩn các nút vòng đời, không tắt Enabled
mặc định true của Button. Bộ thử chờ Enabled nên thỏa ngay trước khi dữ liệu về;
PerformClick trên nút chưa hiện không mở dialog, sau đó hết thời gian chờ.
Ca hồi quy Lifecycle buttons start disabled... thất bại trên bản trước sửa.

Đã sửa GUI: mọi nút vòng đời khởi tạo Disabled; mỗi lần tải lại xóa snapshot cũ
và tắt các nút ghi trước await. Sau tải chỉ bật theo AllowedActions của BLL.
Đã sửa helper: chờ Visible + Enabled + CanSelect, chờ thao tác ghi hoàn tất;
nhánh lỗi có thông tin dialogSeen/submitted để phân biệt lỗi điều khiển với lỗi ghi.
Không tăng thời gian chờ để che lỗi. Không sửa BLL/DAL/DTO/database/tests của Mac.

Hồi quy sau sửa: 53/53, gồm 6 ca mới về tải chậm, chỉ mở khi được phép,
bỏ quyền cũ khi tải lại và đóng form khi dữ liệu về muộn. Build solution 0 lỗi/0 cảnh báo.
Bản chạy và DLL 131 SQL ở bin/WinM33BLoadingFix; bộ thử ở verified-loading-fix.
Cần chạy lại Kiem-tra-WIN-M33B-Administrator.cmd bằng Administrator; chưa tính đạt SQL mới.
Các thay đổi vẫn chưa commit/push.