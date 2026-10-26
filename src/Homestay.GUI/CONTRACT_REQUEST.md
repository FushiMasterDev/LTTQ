# Yêu cầu StartupService — đã xử lý

MAC đã cung cấp StartupService.GetSchemaVersionsAsync trong 77be8d0.
WIN đã nối ReadSchemaVersionsAsync dùng CreateFactory chung với runtime.
Kiểm SQL thật: trả đúng 1,2,3,4,10,20; ca SQL của MAC đạt.
Không còn chặn khởi động do thiếu hợp đồng. Các bước kiểm tay F04/đăng nhập/F18/F24 vẫn cần nghiệm thu.

## Cần bổ sung cho W2: lookup người nhận giao lại đơn

ReassignBookingRequest nhận AssignedEmployeeId; NV có BookingAssign theo phạm vi.
StaffService.ListAsync yêu cầu EmployeeManage nên NV không thể dùng API đó để lấy
danh sách người nhận. Hiện chỉ QL có dropdown người nhận qua StaffService; NV ẩn nút.
Cần API BLL trả Lookup nhân viên Staff/Manager Active được phép nhận đơn,
dùng BookingAssign và phạm vi đơn, không nới quyền EmployeeManage hay đọc DAL từ form.
Chưa sửa DTO/BLL/DAL. Chưa gửi yêu cầu ra dịch vụ ngoài.
