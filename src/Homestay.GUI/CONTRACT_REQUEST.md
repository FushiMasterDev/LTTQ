# Hợp đồng WIN — cập nhật sau M3.3B

StartupService.GetSchemaVersionsAsync đã cung cấp tại 77be8d0, nối F04.
BookingService.ListAssigneesAsync đã cung cấp tại 1c895b2, F08/F09 dùng lookup
người nhận theo quyền và phạm vi đơn, không dùng StaffService để vượt quyền.

M3.3B tại e875b03 đã cung cấp đủ năm hàm ghi còn thiếu. WIN đã nối F11 gửi hủy,
F20 duyệt/từ chối, F09 hủy do homestay và F29 ghi đã chi hoàn.
Không còn khóa toàn bộ các thao tác này vì thiếu triển khai.
Bộ hồi quy giao diện M33B đạt 37/37; nghiệm thu SQL Windows đang chờ chạy.

## Còn cần hợp đồng cho các đợt tiếp theo

- F08: lọc khu vực/người phụ trách và lookup homestay theo phạm vi để dùng
  bộ lọc HomestayId hiện có. Không lọc client trên một trang rồi coi là toàn bộ.
- Tài khoản nhận hoàn: hợp đồng hiện tại giữ NULL nếu chưa lưu. F29 cho ghi nhận
  giao dịch đã chi với xác nhận đối chiếu người nhận/số tiền và mã giao dịch thật;
  không tự suy tài khoản nhận từ chủ/người ghi nhận. Muốn quản lý thông tin nhận hoàn
  cần MAC bổ sung hợp đồng; GUI không tự ghi SQL.
- Đợt 4–7 theo WORK_PLAN chưa có dịch vụ; form preview không tính hoàn thành.

Không sửa DTO/BLL/DAL, database hoặc tests/. Chưa gửi Issue/tin nhắn ra ngoài.

## WIN đã chuẩn bị biểu mẫu M4.1

ListingDraftEditors đã có Owner/Homestay/Contract/RoomType/Room/Price/Block,
nhận Lookup từ bên gọi, trả Values có kiểu nhập rõ ràng. Chưa có dữ liệu thật và
chưa đưa các form này vào EnabledProductionForms. Các nút Tag=layout-editor chỉ ở layout.
Khi nối M4.1 cần tháo nút preview khỏi constructor chạy thật, map Values vào DTO public;
commissionPercent là phần trăm trên màn hình (12.5), không phải tỷ lệ đã chia 100.
Ngày hiệu lực lấy/kiểm lại theo DbClock ở BLL; UI không tự cho phép sửa giá hiện hành.
Không tự duyệt chủ/homestay, không suy ngân hàng, không ghi thẳng SQL hoặc bỏ RowVer.
Ảnh/giấy tờ/tiện nghi/chính sách/điều kiện duyệt vẫn chờ hợp đồng, chưa tính xong W4.