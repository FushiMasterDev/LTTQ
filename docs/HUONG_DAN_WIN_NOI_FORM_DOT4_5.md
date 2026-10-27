# Hướng dẫn WIN nối form Đợt 4 và Đợt 5

Phía MAC đã xong toàn bộ BLL/DAL/DTO của Đợt 1–6, mọi hàm đều chạy thật (không còn hàm `NotImplemented`), 1461 test đạt trên SQL Server thật (commit `32b2c5e`). Phạm vi đồ án dừng ở Đợt 6 — **không làm Đợt 7–8** (F25 Báo cáo, F26 Nhật ký, tab Hiệu suất).

Việc còn lại của WIN: nối dữ liệu thật cho các form dưới đây, bật chúng lên menu, rồi chụp ảnh cho chương 2.

| Đợt | Form | Tài liệu chi tiết (bảng hàm → quyền → DTO → lỗi) |
|---|---|---|
| 4 | F22 Chủ homestay, F15 Danh sách homestay, F16 Hồ sơ homestay, F23 Hợp đồng, F06 tab Lịch (khóa phòng), F29 tài khoản nhận hoàn | `docs/M4.1_CONTRACT_NOTES.md`, `docs/M4.2A_LISTING_WRITE_NOTES.md`, `docs/M4.2B_ROOM_CONTRACT_NOTES.md` |
| 5 | F05 thẻ Quản lý, F08 tab Ngoại lệ + giao nhiều đơn, F18 tab Khu vực / Phiên đăng nhập / chức vụ – nghỉ việc, F19 Giao việc, F14 tab Việc được giao | `docs/M5.1_MANAGER_NOTES.md` |
| 6 | (đã nối) F28, F30, F31, F32 | `docs/M6_ACCOUNTING_NOTES.md` |

File này chỉ là lộ trình và các điểm dễ sai. Khi cần tên trường, câu lỗi, điều kiện chính xác thì mở file notes tương ứng.

---

## Bước 0 — Chuẩn bị

1. `git pull --rebase origin dev` (cần ít nhất commit `32b2c5e`).
2. **Cập nhật database** vì quyền `SettlementItems` đã đổi (spec D12). Chọn một:
   - Cài lại database bằng `database/install_demo.sql` mới (dùng SQLCMD variables như trước, không ghi mật khẩu vào Git); hoặc
   - Chạy bằng tài khoản quản trị trên database chính:
     ```sql
     REVOKE DELETE ON OBJECT::dbo.SettlementItems FROM app_homestay;
     GRANT DELETE ON OBJECT::dbo.SettlementItems TO app_homestay;
     ```
3. Build solution 0 lỗi 0 cảnh báo.

---

## Bước 1 — Lắp service vào `AppServices.cs`

`AppServices.cs` là chỗ **duy nhất** của GUI được dùng DAL (spec 3.5). Thêm vào `CreateRuntime`, dùng lại các biến đã có (`factory`, `current`, `authorizer`, `scope`, `writes`, `clock`, `audit`, `availability`, `notifications`, `evidence`):

```csharp
var owners    = new OwnerService(authorizer, scope, factory, new OwnerRepository());
var homestays = new HomestayService(authorizer, current, scope, factory, new HomestayRepository(), clock);
var rooms     = new RoomService(authorizer, scope, factory, new RoomRepository(), clock, availability);
var contracts = new ContractService(authorizer, scope, factory, new ContractRepository());
var tasks     = new TaskService(authorizer, current, scope, factory, writes,
                    new TaskRepository(), notifications, clock, audit, MachineName);
```

- Thêm 5 service này vào record `Runtime` và thêm property công khai (`Owners`, `Homestays`, `Rooms`, `Contracts`, `Tasks`) như các service cũ.
- **Sửa dòng `FileService`** — phải truyền thêm `scope`, nếu không thì xem ảnh/giấy tờ homestay sẽ ném `InvalidOperationException`:
  ```csharp
  new FileService(authorizer, factory, evidence, scope),
  ```
- `StaffService`, `SessionService`, `BookingService`, `PaymentService` **giữ nguyên constructor**; các hàm mới của Đợt 4–5 đã nằm sẵn trong chúng.

---

## Bước 2 — Quy tắc chung khi nối

1. **Nút bật theo `AllowedActions` của BLL**, không tự suy từ vai trò/trạng thái:
   - Homestay: `HomestayDetail.AllowedActions` (`HomestayAction`: `Edit`, `SetAmenities`, `ManageImages`, `ManageDocuments`, `ManageRooms`, `AddPrice`, `ManageCancellationPolicy`, `Submit`, `Approve`, `Return`, `Suspend`, `Reopen`, `End`, `BlockRoom`).
   - Ngoại lệ F08: `BookingExceptionItem.AllowedActions`.
   - Tắt mọi nút ghi trước khi `await` tải dữ liệu (như đã sửa ở F09).
2. **RowVer**:
   - Homestay và **mọi thứ con của nó** (tiện nghi, ảnh, giấy tờ, chính sách hủy, loại phòng, phòng, giá, khóa phòng, hợp đồng) dùng chung **một token: `HomestayDetail.Homestay.RowVer`**. Mỗi lần ghi con xong token đổi → **luôn gọi lại `Homestays.GetAsync` sau mỗi thao tác ghi** rồi mới cho ghi tiếp.
   - Chủ homestay: `OwnerItem.RowVer` là chuỗi 32 byte do server tính, chỉ giữ rồi gửi lại, không tự tạo.
   - Việc (`WorkTaskItem`), nhân viên (`EmployeeAreasDetail`), đơn (`BookingDetail`), phiếu (`PaymentQueueItem`) dùng RowVer riêng của chúng.
   - Gặp `ConcurrencyException` → báo câu của exception rồi tải lại, không tự gửi lại.
3. **Không gửi lại chuỗi đã che** (CCCD, số tài khoản dạng `****1234`) vào request sửa. Với `UpdateOwnerRequest`: để `IdNumber = null` và các trường ngân hàng `null` nghĩa là **giữ nguyên**; đổi ngân hàng phải dùng `ChangeBankAsync` (chỉ QL).
4. **Ngày giờ**: ngày hiệu lực/hôm nay lấy theo giờ máy chủ (`services.ServerNowAsync()` + 7 giờ), không dùng đồng hồ máy trạm. Gửi `DateTime` luôn là UTC.
5. **Lỗi**: mọi gọi BLL đi qua `ErrorHandler.RunAsync`/`RunMutationAsync` như các form Đợt 2–3; câu lỗi tiếng Việt đã có sẵn trong exception, GUI chỉ hiện.
6. **Bật form lên menu**: thêm mã form vào `ScreenRegistry.EnabledProductionForms` và thêm `entries.Add(...)` có kiểm `gateway.Can(Permission...)` đúng như F18/F24. Gỡ các nút `Tag="layout-editor"` (`ListingDraftPreview.Add`) khỏi constructor chạy thật — chỉ giữ ở chế độ `--preview`.
7. **Danh sách lấy từ BLL**: các ô chọn khu vực/chủ/homestay/danh mục lấy từ hàm `Lookup` của service (mục dưới), không hardcode, không lọc trên một trang ở client.

---

## Bước 3 — Đợt 4: hồ sơ homestay

Thứ tự nên làm: **F22 → F15 → F16 → F23 → F06 tab Lịch → F29**. Làm xong là demo được trọn luồng 9.10 "đưa homestay vào bán".

### F22 Chủ homestay — `services.Owners`

| Thao tác | Hàm | Ghi chú |
|---|---|---|
| Danh sách, tìm | `ListAsync(OwnerSearchRequest)` | phân trang ở DB |
| Chi tiết | `GetAsync(ownerId)` | CCCD/số TK đã che theo vai trò |
| Tạo | `CreateAsync(CreateOwnerRequest)` | NV/QL; luôn tạo `Pending` |
| Sửa | `UpdateAsync(UpdateOwnerRequest)` | NV chỉ sửa chủ `Pending`; xem quy tắc 3 ở trên |
| Đổi ngân hàng | `ChangeBankAsync(ChangeOwnerBankRequest)` | chỉ QL, bắt buộc ghi chú xác minh |
| Duyệt / ngừng | `ChangeStatusAsync(OwnerStatusRequest)` | chỉ QL; duyệt cần đủ thông tin ngân hàng |

Map biểu mẫu `ListingDraftEditors.Owner()`: `name→FullName`, `phone→Phone`, `email→Email`, `address→Address`, `identity→IdNumber`, `bank→BankName`, `account→BankAccountNo`, `accountName→BankAccountName`, `note→Note`.

### F15 Danh sách homestay — `services.Homestays`

- `ListAsync(HomestaySearchRequest)` — lọc trạng thái/khu vực/chủ; **tab "Chờ duyệt"** cho QL = lọc `Status = PendingReview`.
- Lookup cho ô lọc/biểu mẫu: `ListAreasAsync`, `Owners.LookupAsync`, `ListAmenitiesAsync`, `ListRoomCategoriesAsync`.
- Nút "Tạo nháp": `CreateDraftAsync(CreateHomestayRequest)` → mở F16 của homestay vừa tạo.

Map `ListingDraftEditors.Homestay(owners, areas)`: `owner→OwnerId`, `area→AreaId`, `name→Name`, `address→Address`, `phone→ContactPhone`, `checkin→CheckinTime`, `checkout→CheckoutTime`, `description→Description`, `guide→CheckinGuide`.

### F16 Hồ sơ homestay — `services.Homestays` + `services.Rooms`

Tải: `Homestays.GetAsync(homestayId)` trả `HomestayDetail` đủ mọi tab (thông tin, tiện nghi, ảnh, giấy tờ, loại phòng/phòng, giá, chính sách hủy, hợp đồng, lịch sử, danh sách điều kiện duyệt, `AllowedActions`).

| Tab | Hàm ghi | Ghi chú |
|---|---|---|
| Thông tin | `Homestays.UpdateAsync(UpdateHomestayRequest)` | Homestay `Active` mà đổi địa chỉ/khu vực → tự về **Chờ duyệt lại** (D11), hiện thông báo cho người dùng |
| Tiện nghi | `SetAmenitiesAsync` | gửi cả tập tiện nghi |
| Ảnh | `AddImageAsync`, `UpdateImageAsync`, `RemoveImageAsync` | đúng một ảnh bìa; xem ảnh qua `Files.GetHomestayImageAsync` |
| Giấy tờ | `AddDocumentAsync`, `RemoveDocumentAsync` (bắt buộc lý do) | gỡ = ngừng hiện hành, không mất dòng; giấy phép/PCCC của homestay `Active` → Chờ duyệt lại; xem tệp qua `Files.GetHomestayDocumentAsync` |
| Loại phòng & phòng | `Rooms.CreateTypeAsync`, `UpdateTypeAsync`, `SetTypeActiveAsync`, `CreateRoomAsync`, `UpdateRoomAsync`, `SetRoomActiveAsync` | **sửa phòng không đổi loại** → khóa ô "Loại phòng" khi sửa; không tắt được phòng/loại cuối cùng của homestay `Active` |
| Bảng giá | `Rooms.ListPricesAsync`, `AddPriceAsync`, `UpdatePriceAsync` | mỗi phiên bản **ba mức** (ngày thường/cuối tuần/lễ); phiên bản đã hiệu lực (`EffectiveFrom ≤ hôm nay`) **không sửa được** — khóa nút sửa |
| Chính sách hủy | `SetCancellationPolicyAsync`, `DisableCancellationPolicyAsync` | |
| Hợp đồng | xem `HomestayDetail.Contracts`; thao tác ở F23 | NV không thấy hợp đồng (`CanViewContracts=false`) |
| Lịch sử | `HomestayDetail.Audit` | |
| Nút trạng thái | `SubmitAsync`, `ApproveAsync`, `ReturnAsync` (lý do), `SuspendAsync` (lý do), `ReopenAsync`, `EndAsync` | |

**Duyệt**:
- Hiện `HomestayDetail.Approval` (9 điều kiện, mỗi dòng có câu tiếng Việt đạt/chưa đạt) ngay trên tab, để QL thấy còn thiếu gì trước khi bấm.
- Bấm duyệt khi còn thiếu → `BusinessException` liệt kê đúng các điều kiện thiếu.
- QL **không duyệt được hồ sơ do chính mình tạo** → nút `Approve` không có trong `AllowedActions`; hiện câu "cần Quản lý khác duyệt".

**Chú ý kiểu dữ liệu**:
- Có **hai** kiểu tên `RoomPriceVersion`: `Homestay.DTO.Rooms.RoomPriceVersion` (nhóm ba mức, dùng ở F16) và `Homestay.DTO.Availability.RoomPriceVersion` (một mức, dùng khi tính giá). Dùng tên đầy đủ hoặc alias.
- Map `ListingDraftEditors.RoomType`: `category→CategoryId`, `name→Name`, `guests→MaxGuests`, `area→AreaM2` (số nguyên), `description→Description`. `HomestayId` lấy từ form F16.
- Map `ListingDraftEditors.Price`: ba ô `weekday/weekend/holiday` thành **ba mức** trong cùng một request (không phải ba request).

### F23 Hợp đồng — `services.Contracts`

| Thao tác | Hàm | Ghi chú |
|---|---|---|
| Danh sách / chi tiết | `ListAsync`, `GetAsync`, lookup `ListHomestaysAsync` | |
| Tạo | `CreateAsync(CreateContractRequest)` | chỉ QL; tạo là `Active` ngay; mỗi homestay **một** hợp đồng `Active` |
| Kích hoạt lại / gia hạn / chấm dứt | `ActivateAsync`, `ExtendAsync` (chỉ tăng ngày kết thúc), `TerminateAsync` (bắt buộc lý do) | chấm dứt bị chặn khi còn đơn đang giữ phòng |

Map `ListingDraftEditors.Contract`: `homestay→HomestayId`, `number→ContractNo`, `start→StartDate`, `end→EndDate`, `commissionPercent→CommissionRate` (xem dòng dưới); `RowVer` là token của homestay.

**Hoa hồng**: biểu mẫu nhập phần trăm (`12.5`) → gửi `CommissionRate = 12.5m / 100m` (`0.125`). Khi hiển thị thì nhân 100. Giới hạn 10–15% do BLL kiểm.

### F06 tab Lịch — khóa / mở khóa phòng (chỉ QL)

- Vẽ lịch: `Rooms.GetCalendarAsync` (đã có ô `RoomBlockId` và trạng thái `MaintenanceBlock`). Không tự tính chiếm phòng.
- Danh sách khóa: `Rooms.ListBlocksAsync(RoomBlockSearchRequest)`.
- Khóa: `Rooms.BlockAsync(BlockRoomRequest)` — khoảng `[Từ ngày, Đến trước ngày)`; trùng đơn → câu lỗi có **mã đơn**, hiện nguyên câu.
- Mở khóa: `Rooms.UnblockAsync(UnblockRoomRequest)` — bắt buộc lý do.
- Nút chỉ hiện khi `Can(Permission...)` của QL; NV gọi sẽ bị `ForbiddenException`.

### F29 tab Hoàn tiền — lưu tài khoản nhận hoàn (chỉ Kế toán)

- `Payments.UpdateRefundAccountAsync(UpdateRefundAccountRequest)` → trả `RefundAccountResult` (số TK đã che + RowVer mới).
- Sau khi lưu, gọi `RecordRefundPaidAsync` với **RowVer mới** trả về.

---

## Bước 4 — Đợt 5: Quản lý

### F05 thẻ Quản lý

`Bookings.GetManagerDashboardAsync()` → `ManagerDashboard`: nhận/trả phòng hôm nay, đơn chưa giao, số ngoại lệ theo 4 loại, việc quá hạn, phiếu chờ xác minh, yêu cầu hủy chờ duyệt. Bấm thẻ → mở đúng tab/danh sách tương ứng (số đếm khớp danh sách vì dùng chung truy vấn).

### F08 tab Ngoại lệ + giao nhiều đơn

- Tab Ngoại lệ dùng **grid riêng**: `Bookings.ListExceptionsAsync(BookingExceptionSearchRequest)`. Mỗi dòng một vấn đề (`Kind`: quá hạn chủ xác nhận, giữ phòng sắp hết, phiếu thu chờ lâu, đơn chưa giao), cột "Vấn đề" từ `Problem`, cột "đã quá bao lâu" từ `ElapsedSeconds`/`OverdueSeconds`. Mỗi dòng có sẵn `RowVer` của đơn để giao.
- Nút trên dòng theo `AllowedActions`: mở đơn (F09), giao, tạo việc (mở F19 kèm `BookingId`).
- **Giao nhiều đơn**: cho chọn nhiều dòng → chọn người nhận (`Bookings.ListAssigneesAsync` của từng đơn hoặc lookup chung trong notes) → `Bookings.AssignManyAsync(new AssignManyBookingsRequest(items, employeeId))`, `items` là `(BookingId, RowVer)`. Tất cả hoặc không: một đơn lỗi thì **không đơn nào** được giao, câu lỗi nêu mã đơn → tải lại.

### F18 Nhân viên & tài khoản — 3 phần mới

| Phần | Hàm | Ghi chú |
|---|---|---|
| Tab Khu vực | `Staff.GetAreasAsync(employeeId)`, `Staff.SetAreasAsync(SetEmployeeAreasRequest)`, `Staff.ListByAreaAsync` | "Toàn khu vực" chỉ cho Quản lý; Kế toán không có khu vực; có hiệu lực ngay cả khi người đó đang đăng nhập |
| Tab Phiên đăng nhập | `Sessions.ListOpenSessionsAsync`, `Sessions.ListSessionHistoryAsync`, `Sessions.ForceLogoutAsync(ForceLogoutRequest)` | bắt buộc lý do; không tự buộc đăng xuất chính mình |
| Đổi chức vụ / nghỉ việc | `Staff.ChangePositionAsync`, `Staff.TerminateEmploymentAsync` | bắt buộc lý do; không tự hạ/tự cho nghỉ; luôn còn ít nhất một QL. Nghỉ việc: đơn đang giữ trả về chưa giao, việc đang mở bị hủy, QL nhận thông báo — hiện cảnh báo này trong hộp xác nhận |

### F19 Giao việc (QL) và F14 tab Việc được giao (người nhận) — `services.Tasks`

| Ai | Hàm |
|---|---|
| QL | `ListAssignedAsync`, `GetAsync`, `ListAssigneesAsync`, `CreateAsync(WorkTaskValues)`, `UpdateAsync`, `CancelAsync` (lý do) |
| Người nhận | `ListMineAsync`, `StartAsync`, `CompleteAsync(CompleteWorkTaskRequest)` (**bắt buộc ghi kết quả**) |

- Hạn chót nhập giờ VN → gửi UTC. Việc quá hạn có cờ trên `WorkTaskItem` → tô đỏ dòng.
- Gắn đơn/homestay tùy chọn; mở F19 từ F08 thì điền sẵn `BookingId`.

---

## Bước 5 — Kiểm tra trước khi chụp ảnh

Chạy trên database mẫu, mỗi luồng một lần:

1. **Luồng 9.10**: NV tạo chủ → QL duyệt chủ → NV tạo homestay nháp, thêm tiện nghi, ảnh bìa, giấy phép, PCCC, loại phòng, phòng, giá → QL tạo hợp đồng → NV gửi duyệt → **QL khác** duyệt (thử bấm khi còn thiếu để thấy danh sách thiếu) → homestay hiện ở F06 tra phòng và tạo được đơn.
2. QL khóa một phòng trùng đơn đã có → thấy mã đơn trong câu lỗi; khóa đêm trống → lịch F06 đổi màu; mở khóa → trống lại.
3. QL giao nhiều đơn từ tab Ngoại lệ; gán khu vực cho một NV rồi đăng nhập NV đó kiểm phạm vi; giao việc → NV bắt đầu → xong kèm kết quả; buộc đăng xuất một phiên.
4. Mở mọi form mới với từng vai trò (NV, QL, KT) để chắc nút ẩn/hiện đúng quyền.

Sau đó cập nhật bảng form trong `WORK_PLAN.md` (W4.x, W5.x) và chụp ảnh `docs/screenshots/Fxx_<vaitro>_<buoc>.png` cho chương 2.

Gặp hàm thiếu hoặc lỗi BLL: ghi vào `src/Homestay.GUI/CONTRACT_REQUEST.md` rồi push, MAC sẽ xử lý. **Không** tự viết SQL trong form.
