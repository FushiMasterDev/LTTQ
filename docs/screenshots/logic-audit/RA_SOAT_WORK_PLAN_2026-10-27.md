# Rà soát chức năng và WORK_PLAN — 27/10/2026

## Kết luận

Chưa hoàn thành toàn bộ WORK_PLAN. Bản gốc đối chiếu là commit 1654c06 trên dev.
Phần đang hoạt động là nền tảng và phần lớn đợt 2. Các lớp dịch vụ hiện có chỉ
bao phủ Auth, Session, Startup, Staff, Catalog, Availability, Customer, Booking,
Payment.RecordReceipt, Notification, File và JobRunner hết hạn giữ phòng.
Có 29 form có thể mở/render trong chế độ preview; điều đó không chứng minh
29 form đều có nghiệp vụ thật. Menu chạy thật vẫn ẩn các phần chưa tích hợp.

Rà soát này không sửa DTO/BLL/DAL, database, tests/ hoặc ô việc của MAC.
Không chạy lại seed hay thay dữ liệu/mật khẩu database chính.

## Các lỗi logic đã sửa

1. ErrorHandler trước đây luôn bật lại nút sau await, ghi đè trạng thái do
   AllowedActions vừa trả về. Nút nhận phụ trách/hủy/thu cọc nay giữ đúng quyền
   và trạng thái sau thao tác hoặc sau tải lại khi RowVer xung đột.
2. F09 khóa nút ghi trước lần tải đầu, dùng AsyncLoader để không áp dụng dữ liệu
   trả về muộn hoặc dữ liệu sau khi tab đã đóng.
3. F07 hủy báo giá/danh sách phòng cũ khi đổi ngày, khách hoặc loại phòng;
   xóa phòng và giá cũ, chỉ cho lưu khi có báo giá hiện tại. Kết quả cũ dù
   bỏ qua cancellation cũng không được áp dụng. Tìm khách chậm không ghi đè
   lựa chọn Khách mới, và được hủy khi bắt đầu lưu/đóng form.
4. Chặn đóng form hoặc gọi thao tác ghi thứ hai khi thao tác ghi đầu chưa xong;
   thao tác thu tiền thành công vẫn được phép tự đóng form.
5. F08 xuất toàn bộ danh sách theo bộ lọc và khóa/thứ tự sắp xếp hiện tại.
6. F13 cho Nhân viên yêu cầu đổi SĐT của khách chưa có đơn. Khách đã có đơn
   vẫn yêu cầu quyền CustomerPhoneChange, BLL tiếp tục kiểm tra lại.
7. Tìm nhanh F03 ngăn mở nhiều hộp kết quả do nhấn Enter liên tiếp và bỏ kết
   quả khi phiên đang kết thúc/khóa.
8. F2 gọi hành động chi tiết của lưới, Ctrl+F tập trung ô lọc của màn hiện tại.
9. F06 bấm đúp ô trống mở tạo đơn theo loại phòng và ngày của ô; phòng cụ thể
   được chọn ở F07, không cam kết giữ phòng chỉ bằng thao tác xem lịch.

## Kết quả kiểm tra của lượt này

| Kiểm tra | Kết quả | Giới hạn |
|---|---|---|
| Build toàn solution Release, đầu ra riêng từng project | 0 lỗi, 0 cảnh báo | Dùng SDK 10.0.401 trên Windows |
| Homestay.Rules.Tests | 274 đạt / 274 | Công thức, trạng thái, quyền, đầu vào; không thay thế SQL |
| GUI smoke/preview | 74 đạt / 74 | Có render 29 form ở DPI144; chưa đủ 100/125/150% và Designer |
| Hồi quy logic mới | 18 đạt / 18 | Quyền nút, chống ghi chồng, đóng form, kết quả cũ, báo giá, F2/Ctrl+F |
| SQL integration do người dùng chạy Administrator | 36 PASS + RESULT PASS + Cleanup PASS | Database HomestayWinCheck_958ab45aad2b4804 đã được xóa đúng sau chạy |
| Homestay.BLL.Tests | 80 bị bỏ qua (Skip), 0 được chạy | Chưa cấp HOMESTAY_TEST_CONNECTION của database thử cho bộ này; không tính là đạt |
| SQL chính chỉ đọc | PASS | 38 bảng, 7 tài khoản, 24 đơn; Unicode, Vietnamese_CI_AS, TransferredAt đúng; app không sysadmin/db_owner |

Hai lần đầu dotnet test không kết nối được testhost do thiếu quyền kết nối nội bộ.
Sau khi cấp quyền mạng cho lượt kiểm tra, Rules chạy đạt; BLL vẫn Skip đúng
theo SqlFactAttribute vì chưa có biến kết nối database thử. Không dùng database
chính để chạy 80 ca có ghi dữ liệu.

SQL integration đã thử tài khoản 3 vai trò, đổi mật khẩu bắt buộc/heartbeat,
tạo/khóa/mở/đặt lại mật khẩu nhân viên ở DB tạm, đọc 6 tab danh mục, sửa danh mục
và tham số, tra phòng/lịch, khách hàng che CCCD, tạo đơn, tìm đơn, nút Save F07,
nút Save F10, chống cọc trùng, thông báo kế toán, job và logout.
Đây là 36 ca của bộ thử riêng WIN, không phải 80 ca xUnit SQL của MAC.

## Đối chiếu từng việc WIN

| Mục | Tình trạng | Căn cứ và phần còn lại |
|---|---|---|
| W1.0 | Hoàn thành | VS2026/SDK10/SQL Express có sẵn; build và kết nối thật đạt |
| W1.1 | Hoàn thành một phần | GUI/sln/tham chiếu đúng, build Windows đạt; chưa xác nhận lại build trên MAC/Designer |
| W1.2 | Hoàn thành một phần | Startup/schema, DPAPI, logger/bắt lỗi đã có và được thử; còn nghiệm thu tay F04/ca lỗi cấu hình |
| W1.3 | Hoàn thành | Control chung, phân trang, hiện cột, CSV BOM, tìm không dấu, tiền/ngày, AsyncLoader có kiểm thử |
| W1.4 | Hoàn thành một phần | F01/F02/F03, vai trò, phiên và đổi mật khẩu chạy; chưa nghiệm thu đủ 5 lần sai/khóa và thay phiên ở GUI |
| W1.5 | Hoàn thành một phần | F18 tab NV, F24 đủ dịch vụ; cần kiểm tay đầy đủ các nút/ngoại lệ, không gồm tab đợt 5 |
| W2.1 | Hoàn thành | Dashboard Nhân viên dùng số liệu SQL, thẻ và điều hướng theo hàng đợi; không gồm dashboard QL/KT |
| W2.2 | Hoàn thành một phần | Tra phòng/lịch 14/31 ngày có dữ liệu, giá và policy; còn kiểm tay ô trống, chú giải và lọc lịch theo homestay |
| W2.3 | Hoàn thành | Danh sách/lịch sử khách, tạo/sửa/đổi SĐT qua BLL, che giấy tờ; kiểm đọc và quyền theo vai trò |
| W2.4 | Hoàn thành | Báo giá, Save tạo đơn thật, snapshot tiền/mã/chuyển khoản, thời hạn SQL và đồng hồ; chưa có voucher đúng kế hoạch |
| W2.5 | Hoàn thành một phần | F08/F09/F10 đọc/ghi thật và RowVer; còn lookup người nhận giao lại đơn cho NV, bộ lọc đầy đủ và đếm ngược từng đơn |
| W2.6 | Hoàn thành một phần | Ctrl+K/tìm nhanh và thông báo có dữ liệu; còn nghiệm thu tay đọc tất cả/phân trang thông báo |
| W3.1 | Chưa làm | F27 chỉ bố cục; thiếu API hàng chờ/xác minh/từ chối |
| W3.2 | Chưa làm | Có tab đến/đi và chờ chủ đọc dữ liệu; thiếu nút xác nhận/từ chối/đổi phòng/nhận/trả/không đến và API |
| W3.3 | Chưa làm | F11/F20 chỉ bố cục; chưa có ChangeRequestService hủy và duyệt/hoàn |
| W3.4 | Chưa làm | F29 hoàn tiền chỉ bố cục; chưa có API chi hoàn |
| W4.1 | Chưa làm | F22 chỉ bố cục; thiếu OwnerService |
| W4.2 | Chưa làm | F15 chỉ bố cục; thiếu HomestayService |
| W4.3 | Chưa làm | F16 các tab chỉ bố cục; thiếu nghiệp vụ hồ sơ/phòng/giá/duyệt |
| W4.4 | Chưa làm | F23 chỉ bố cục; thiếu ContractService |
| W4.5 | Chưa làm | Nút khóa/mở phòng đang ẩn; chưa có RoomService/contract |
| W5.1 | Chưa làm | F05 hiện dashboard NV; chưa có dashboard QL/ngoại lệ/giao nhiều đơn |
| W5.2 | Chưa làm | Tab khu vực/phiên và đổi chức vụ/nghỉ việc còn ẩn |
| W5.3 | Chưa làm | F19/F14 chỉ bố cục; chưa có TaskService |
| W6.1 | Chưa làm | Dashboard kế toán chưa có dịch vụ tài chính |
| W6.2 | Chưa làm | F28 chỉ bố cục; chưa có nhập tay/tự khớp sao kê |
| W6.3 | Chưa làm | F30/F31 chỉ bố cục; chưa có SettlementService |
| W6.4 | Chưa làm | F32 chỉ bố cục; chưa có sổ thu chi/công nợ |
| W6.5 | Chưa làm | DocumentPrinter có smoke test phân trang; chưa nối phiếu thu/chi/đối soát thật hoặc tiền bằng chữ |
| W7.1 | Chưa làm | F25 chỉ bố cục; chưa có ReportService |
| W7.2 | Chưa làm | F26/F14 chỉ bố cục; chưa có API audit/hiệu suất |
| W7.3 | Hoàn thành một phần | Có idle lock, phím tắt và lưu cột cho một số lưới; còn Help đầy đủ, tất cả lưới và ma trận DPI/1366x768 |
| W8.1 | Chưa làm | Ảnh render preview không phải chuỗi ảnh thao tác thật cho mọi luồng/vai trò |
| W8.2 | Hoàn thành một phần | Có ghi nhận vai trò và kết quả WIN; thiếu bộ ảnh cây VS/menu và nghiệm thu DPI theo yêu cầu |

Tình trạng “Hoàn thành” giới hạn theo đúng việc/đợt trong cột tương ứng.
Ví dụ W2.1 hoàn thành không có nghĩa F05 đã có phần của Quản lý và Kế toán.
Không đánh dấu hoàn thành cả W1/W2 vì còn điều kiện nghiệm thu và hợp đồng thiếu.

## Đối chiếu phần MAC và các yêu cầu để lại

WORK_PLAN đang đánh dấu M1.1–M1.4 và M2.1–M2.4 hoàn thành. Mã hiện có
schema/seed/hạ tầng và các dịch vụ của những mốc đó; chưa tái nghiệm thu trọn
80 ca SQL, 10 luồng cạnh tranh và mọi ca khóa/từ chối trong lượt này.
Không thay các ô MAC hoặc kết luận thay chủ phần việc đó.

Các mốc M3.1–M3.3, M4.1–M4.2, M5.1, M6.1–M6.2, M7.1–M7.2,
M8.1–M8.2 vẫn chưa hoàn thành. Chưa có các service/DTO ghi công việc tương ứng.
BookingStateMachine đã có bảng chuyển trạng thái nhưng không đồng nghĩa
đã cài đặt API và transaction cho toàn bộ vòng đời đơn.

Mục 5 của kế hoạch vẫn để lại: đổi ngày/loại, xử lý trùng phòng/khoản bù,
voucher, cọc bổ sung, nhập file sao kê, job ngoài hết hạn giữ phòng,
kiểm nhiều máy/hiệu năng và đóng gói. F12/F21 ẩn, F17 bỏ. Những việc này
không được tính là lỗi thiếu so với phạm vi kế hoạch đã chốt.

## Việc cần làm tiếp theo

1. MAC cung cấp lookup nhân viên nhận giao lại đơn cho Staff theo quyền
   BookingAssign; không mở quyền EmployeeManage để lấy danh sách.
2. Hoàn thiện/kiểm tay các phần còn lại đợt 1–2, chạy đầy đủ 80 ca SQL trên
   database tạm và ma trận quyền/phiên. Chứng từ GUI hiện nhận JPG/PNG;
   BLL còn hỗ trợ WEBP/PDF nhưng trình xem/tải GUI chưa phủ hai định dạng đó.
3. Cung cấp và nối M3.1–M3.3 → W3.1–W3.4 để đơn đi từ cọc qua xác minh
   đến xác nhận/nhận/trả phòng và các nhánh hủy/hoàn. Đây là bước kế tiếp
   để Kế toán có nghiệp vụ thật thay vì chỉ tra cứu.
4. Làm lần lượt đợt 4–7, sau đó W8/M8. Không mở nút giả hoặc bỏ kiểm quyền
   để trình bày các màn chưa có dịch vụ như đã hoàn thành.

## Chạy lại bằng chứng

- Bộ Rules: dotnet test tests/Homestay.Rules.Tests/Homestay.Rules.Tests.csproj
- Bộ hồi quy WIN độc lập: dotnet run --project
  docs/screenshots/logic-audit/checks/GuiAuditChecks.csproj --
  <đường-dẫn-tệp-kết-quả.txt>
- SQL WIN: chạy outputs/Kiem-tra-logic-Administrator.cmd ở máy kiểm thử này
  bằng quyền Administrator. Tệp này ở ngoài Git vì dùng đường dẫn môi trường
  và cấu hình DPAPI của máy; chỉ tạo/dọn database tạm, không cập nhật DB chính.
- Chưa tạo commit/push các sửa đổi của lượt rà soát này tại thời điểm lập báo cáo.
