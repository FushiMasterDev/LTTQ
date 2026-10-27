# Hoàn thiện WIN đợt 2–3 — 27/10/2026

Bản tích hợp dùng hợp đồng MAC fabae8e. Chưa hoàn thành toàn bộ WORK_PLAN.
Các thay đổi WIN trong lượt này đang ở máy cục bộ, chưa commit/push.

## Chức năng đã nối

- F06: lịch lọc homestay, màu trạng thái, ô trống mở F07 đúng phòng/ngày;
  thay bộ lọc hủy kết quả cũ. F07 không tự đổi sang phòng khác khi phòng chọn đã hết.
- F08: đủ 11 hàng đợi, lọc tên khách, xóa bộ lọc, đếm ngược theo giờ SQL;
  F08/F09 giao lại đơn bằng lookup người nhận từ BLL theo quyền.
- F03: thông báo phân trang, số chưa đọc, đọc một/tất cả, mở đơn;
  giữ trang khi tải lại, thử lại khi lỗi, bỏ kết quả sau khi đóng.
- F10: chứng từ JPG/PNG/WEBP/PDF tối đa 5 MB, kiểm chữ ký nội dung,
  xem/xóa trước lưu; PDF/WEBP cho lưu bản gốc để mở ngoài.
- F27: hàng chờ, thông tin đơn/chứng từ; xác minh hoặc từ chối thật qua BLL,
  RowVer của phiếu và đơn, bắt buộc lý do từ chối.
- F09: xác nhận/từ chối thay chủ, đổi phòng cùng loại khi xác nhận,
  nhận/trả phòng, không đến. Quyền và trạng thái quyết định nút được bật.
- F11 xem trước tiền hoàn; F20 xem yêu cầu; F29 xem hàng chờ hoàn.
  Các thao tác ghi còn thiếu dịch vụ được khóa và có giải thích.

## Lỗi sau lượt chạy đã sửa

Ba lần “Chức năng đang làm” do các nút F09 đã nối dịch vụ nhưng còn Tag=pending,
base form gắn thêm thông báo placeholder. Đã bỏ Tag ở constructor chạy thật;
regression kiểm các nút không còn handler thông báo cũ. Giao diện preview vẫn giữ thông báo.

SearchPicker trước đây dùng DataSource khiến Windows tự chọn lại mục đầu khi bỏ chọn
homestay. Đã dùng Items/SelectedIndex ổn định và thêm nút bỏ chọn; kiểm bằng form đã Show
xác nhận SelectedItem=null và lịch trở về đủ phòng.

## Bằng chứng

| Kiểm tra | Kết quả | Phạm vi |
|---|---|---|
| Release toàn solution | 0 lỗi, 0 cảnh báo | Sau hai sửa giao diện cuối |
| SQL WIN | 63 PASS, RESULT PASS, Cleanup PASS | Database tạm; không đổi database chính |
| xUnit SQL | 111/111, 0 Skip | Cùng lượt thử tạm, đã dọn database |
| Hồi quy WIN | 31/31 | Có hai sửa giao diện cuối |
| GUI smoke | 74/74 | Render 29 form ở DPI144; chưa thay nghiệm thu DPI/Designer |

WIN-COMPLETION-M33A-SQL.txt là lượt người dùng vừa chạy: tạo đơn → thu cọc →
xác minh → xác nhận với phòng thay thế → nhận phòng → trả sớm và hoàn thành.
Ba thông báo cũ xuất hiện trong lượt này nhưng không ngăn ghi trạng thái;
sửa thông báo và bỏ chọn homestay được kiểm bằng hồi quy sau lượt SQL,
chưa chạy lại toàn bộ SQL sau hai sửa giao diện đó.

## Việc chưa xong

W2.5 còn các bộ lọc cần lookup/hợp đồng theo phạm vi. W3.3/W3.4 chờ năm hàm ghi:
RecordRefundPaidAsync, CancelHomestayUnavailableAsync,
SubmitCancelAsync, ApproveAsync, RejectAsync. Chưa mở nút để gọi hàm chưa cài đặt.
Đợt 4–7 chưa có đủ dịch vụ; không coi form preview là nghiệp vụ hoàn thành.
W1 còn bằng chứng nghiệm thu thủ công; W7.3/W8 còn DPI/Designer và chuỗi ảnh báo cáo.

Chỉ sửa mã GUI, bằng chứng WIN và ô WIN trong WORK_PLAN; không sửa nguồn MAC.
Không commit cấu hình kết nối, mật khẩu hoặc database.