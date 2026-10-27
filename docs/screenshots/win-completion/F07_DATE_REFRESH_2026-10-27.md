# Sửa F07 trắng loại phòng sau đổi ngày — 27/10/2026

Nguyên nhân: StayChanged xóa danh sách phòng và quote cũ nhưng chưa tự gọi lại
AvailabilityService; người dùng phải bấm Tìm loại phòng mà không có giải thích.

Đã sửa: thay ngày/số khách xóa giá/phòng cũ ngay, chờ ngắn để gom các thay đổi rồi
tự tìm lại trên BLL. Giữ loại phòng cũ nếu vẫn có trong kết quả, yêu cầu chọn lại
khi không còn phù hợp. Không tự đổi sang loại khác. Chưa giữ phòng/chưa ghi đơn.
Ngày trả <= ngày nhận, lỗi tải hoặc hết phòng đều có thông báo cạnh nút tìm;
ô chọn phòng tắt khi chưa có dữ liệu, không mở danh sách trắng. Bấm Tìm loại phòng
vẫn tải lại thủ công. Phải xem báo giá mới trước khi lưu.

Chặn kết quả cũ dù bên đọc bỏ qua cancellation; đóng form hủy tải và timer.
Sửa thêm trường hợp quote không còn phòng mà lựa chọn Auto cũ bật lại nút lưu.

Build toàn solution 0 lỗi/0 cảnh báo. Hồi quy 47/47, trong đó 10 ca mới liên quan
đổi ngày/khách, gộp thay đổi, giữ loại hợp lệ, kết quả chậm, hết phòng, ngày sai,
lỗi tải, đóng form và Auto của quote hết phòng. Các ca tự động dùng dữ liệu tổng hợp
ở ranh giới đọc BLL, chưa thay kiểm SQL Windows. Nghiệm thu SQL M33B vẫn chờ chạy;
không tuyên bố 131 ca SQL đã đạt trên máy này.

Bản chạy: bin/CompletionM33B/bin/Homestay.GUI/release.
Launcher cục bộ Mo-ban-sua-doi-ngay.cmd; đóng bản cũ rồi đăng nhập bản sửa.
Không sửa nguồn MAC/database chính. Chưa commit/push lượt sửa này.