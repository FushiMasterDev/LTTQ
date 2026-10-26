# Yêu cầu StartupService — đã xử lý

MAC đã cung cấp StartupService.GetSchemaVersionsAsync trong 77be8d0.
WIN đã nối ReadSchemaVersionsAsync dùng CreateFactory chung với runtime.
Kiểm SQL thật: trả đúng 1,2,3,4,10,20; ca SQL của MAC đạt.
Không còn chặn khởi động do thiếu hợp đồng. Các bước kiểm tay F04/đăng nhập/F18/F24 vẫn cần nghiệm thu.
