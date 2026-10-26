# Gửi bên MAC: bổ sung điểm khởi động cho WIN

WIN đã tải M1.4 và M2.1/M2.2, đã ghép AuthService/SessionService/StaffService/CatalogService.
Còn chặn W1.2: BLL chưa có hàm đọc dbo.SchemaVersions trước đăng nhập.

Nhờ MAC thêm StartupService(ConnectionFactory) với:
Task<IReadOnlyList<int>> GetSchemaVersionsAsync(CancellationToken cancellationToken = default)

Hàm không cần phiên, chỉ SELECT phiên bản thật trong database và dịch lỗi SQL bằng hạ tầng BLL hiện có.
Không thêm gói, không đổi schema và không sửa GUI. Thêm test phù hợp, công bố chữ ký, build solution,
commit rồi push dev. Chữ ký tương đương có ghi tài liệu cũng được.

WIN sẽ nối AppServices.ReadSchemaVersionsAsync, kiểm đúng tập 1,2,3,4,10,20,
rồi chạy F04, đăng nhập ba vai trò, đổi mật khẩu tạm, F18 và F24 với SQL thật.
Hiện WIN không bỏ kiểm phiên bản và không viết SQL trong GUI để lách hợp đồng.

Yêu cầu này thuộc WORK_PLAN.md mục 1.1. Chưa tạo được GitHub Issue nhãn contract vì kết nối
GitHub hiện trả 403 Resource not accessible by integration.
