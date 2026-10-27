# Ảnh báo cáo — giao diện theo code mới

Code nền GitHub: 8a12384. Giao diện WIN mới được nối trên máy này.
Ảnh được chụp từ WinForms chạy với database SQL tạm, dữ liệu seed của dự án;
database tạm đã dọn sau thử nghiệm. Đây là dữ liệu mẫu, không phải cơ sở/khách ngoài thực tế.

| Ảnh | Nội dung đưa vào báo cáo |
|---|---|
| F22_QuanLy_DanhSachChu.png | Danh sách chủ homestay, ngân hàng và trạng thái |
| F15_QuanLy_DanhSachHomestay.png | Danh sách homestay, khu vực, hợp đồng và số phòng |
| F16_QuanLy_ThongTin.png | Thông tin hồ sơ homestay |
| F16_QuanLy_LoaiPhong.png | Danh sách loại phòng và sức chứa |
| F16_QuanLy_BangGia.png | Ba mức giá theo phiên bản hiệu lực |
| F16_QuanLy_DieuKienDuyet.png | Kiểm tra điều kiện duyệt hồ sơ |
| F23_QuanLy_HopDong.png | Hợp đồng và thời hạn/hoa hồng |
| F19_QuanLy_GiaoViec.png | Danh sách việc đã giao cho nhân viên |
| F14_NhanVien_ViecCuaToi.png | Việc trong phạm vi tài khoản Nhân viên |
| F18_QuanLy_NhanVien.png | Quản lý nhân viên/tài khoản |
| F28_KeToan_SaoKe.png | Các giao dịch sao kê mẫu và trạng thái khớp |
| F30_KeToan_DoiSoat.png | Màn đối soát kỳ hiện tại; kỳ này chưa có bảng, ảnh 0 dòng là trạng thái thực |
| F32_KeToan_SoThuChi.png | Sổ thu chi đã xác minh |

225/225 SQL đạt, 0 Skip; 1176/1176 Rules đạt; hồi quy giao diện 53/53;
kiểm điều khiển/menu theo vai trò 17/17. Build Release 0 lỗi/0 cảnh báo.

Lượt SQL ban đầu dừng sau 225 PASS vì tệp cập nhật bị dính GOCREATE.
Đã sửa tệp và chạy riêng bước cập nhật: REPORT-LATEST-UPDATE.txt có RESULT PASS.
Cả hai log được giữ để không che lượt lỗi; phần cập nhật view/quyền không reset dữ liệu.

Mở ứng dụng: outputs/Mo-giao-dien-moi-nhat.cmd (bản đăng nhập thật, không phải --preview).
Dùng tài khoản/mật khẩu hiện có. Nhân viên/Quản lý xem homestay ở Danh sách homestay,
chọn dòng → Chi tiết; Quản lý có Chủ/Hợp đồng/Giao việc/Nhân viên; Kế toán có sao kê,
chi hoàn, đối soát và sổ thu chi. Các nút được bật theo quyền và trạng thái từ BLL.

Giới hạn: chưa nghiệm thu mọi thao tác UI mới, mọi DPI/Designer; khóa ngay trên lịch,
giao nhiều đơn và một số thao tác metadata ảnh vẫn cần lượt hoàn thiện tiếp.
Các thay đổi WIN của lượt mới chưa commit/push.