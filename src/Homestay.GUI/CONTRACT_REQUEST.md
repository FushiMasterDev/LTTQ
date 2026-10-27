# Hợp đồng WIN — nền 6c0d1bc, hướng dẫn đợt 4–5

Đã có và đã lắp OwnerService, HomestayService, RoomService, ContractService,
TaskService cùng các API M5 trong Staff/Session/Booking. FileService có DataScope.
WIN không tự viết SQL trong form, không thay constructor/hợp đồng backend.

Đã nối các phần còn thiếu: lịch F06 khóa/mở; F08 Ngoại lệ/giao nhiều/mở việc theo đơn;
thẻ Quản lý; F18 khu vực, phiên mở/lịch sử, buộc đăng xuất, chức vụ/tạm nghỉ/nghỉ việc;
F19/F14 tạo/sửa/hủy/bắt đầu/hoàn thành; F16 thông tin ảnh/quyền/D11. F29 nạp lại
phiếu sau lưu tài khoản để dùng token mới. Xem docs/screenshots/win-dot45/STATUS.md.

Chưa có hàm thiếu mới cần MAC viết. Hai điểm đã đối chiếu nguồn để tránh hiểu sai:

- ContractService.ActivateAsync chỉ revalidate hợp đồng Active, không kích hoạt lại
  hợp đồng Terminated. GUI ghi "Kiểm tra lại hiệu lực" theo notes M4.2B.
- WorkTaskValues.DueAtVietnam yêu cầu DateTimeKind.Unspecified; BLL đổi sang UTC.
  GUI giữ đúng hợp đồng DTO này. Không gửi DateTimeKind.Utc vào DueAtVietnam.

Build/1230 Rules/53 hồi quy/37 UI kiểm mới đạt. Lượt SQL cuối v4 đạt 94 bước WIN/SQL và 231/231 BLL, không skip; RESULT PASS và Cleanup PASS. Có 20 ảnh mẫu; xem STATUS.md.
View/quyền D12 trên database chính đã được duyệt và cập nhật trước đây; lượt này
không reset hoặc cập nhật database chính.

Theo hướng dẫn mới, đợt 7–8/F25/F26/Hiệu suất nằm ngoài phạm vi phát triển hiện tại.
Không sửa BLL/DAL/DTO, database/ hoặc tests/ của MAC trong lượt WIN này.
