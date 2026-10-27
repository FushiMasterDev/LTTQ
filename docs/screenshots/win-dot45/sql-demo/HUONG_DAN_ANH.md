# Ảnh giao diện đợt 4–5 cho chương 2

20 ảnh được chụp từ WinForms có dữ liệu SQL thật trong database thử riêng. Tên người, homestay, ngân hàng và số tài khoản trong ảnh là dữ liệu mẫu/kiểm thử. Không dùng database chính để tạo dữ liệu báo cáo.

Thứ tự trình bày gợi ý:

1. F22_QuanLy_DanhSachChu — quản lý hồ sơ chủ.
2. F15_QuanLy_DanhSachHomestay — danh sách homestay.
3. F16_QuanLy_ThongTin / LoaiPhong / BangGia — nội dung hồ sơ.
4. F16_QuanLy_DuDieuKien — đủ 9 điều kiện trước duyệt; F16_QuanLy_DaDuyet — sau duyệt. Luồng thử còn xác nhận homestay vừa duyệt hiện khi tra phòng và tạo được đơn.
5. F23_QuanLy_HopDong — hợp đồng đang hiệu lực.
6. F06_QuanLy_KhoaPhong — ô bảo trì do thao tác khóa thật, sau đó bộ thử đã mở khóa.
7. F08_QuanLy_NgoaiLe — giữ phòng sắp hết; bộ thử chọn hai đơn và giao một lần. Ảnh chụp sau tải lại nên các nút bị khóa vì chưa chọn lại dòng, đúng hành vi ứng dụng.
8. F19_QuanLy_GiaoViec — danh sách việc; F14_NhanVien_ViecCuaToi và HoanThanhViec — phía người nhận.
9. F18_QuanLy_NhanVien / LichSuPhien / TheoKhuVuc — nhân viên, lịch sử phiên có buộc đăng xuất, danh sách theo khu vực.
10. F28_KeToan_SaoKe, F30_KeToan_DoiSoat, F32_KeToan_SoThuChi — bổ sung phần kế toán đang có. F30 kỳ hiện tại không có dòng; không dùng ảnh này để khẳng định đã tạo bảng đối soát trong kỳ đó.

Ảnh `F16_QuanLy_DieuKienDuyet` của homestay seed và ảnh `F16_QuanLy_DuDieuKien` của homestay mới tạo là hai hồ sơ khác nhau. Chọn đúng cặp ảnh DuDieuKien/DaDuyet khi mô tả một luồng.

Giới hạn: ảnh từng form và các luồng đã thử không thay thế nghiệm thu từng nút, DPI 100/125/150% hoặc toàn bộ thao tác VS Designer. Mật khẩu, connection.dat và nội dung database không nằm trong gói ảnh. Tính năng đợt 7–8 nằm ngoài phạm vi hướng dẫn hiện tại.

Kết quả đầy đủ và tình trạng nghiệm thu được ghi trong ../STATUS.md cùng WIN-DOT45-SQL.txt sau khi bộ SQL cuối kết thúc.
