# KẾ HOẠCH CHIA VIỆC — HOMESTAY DESKTOP

| Mục | Nội dung |
|---|---|
| Căn cứ | `HOMESTAY_DESKTOP_SPEC.md` v0.4 (3 vai trò: Nhân viên, Kế toán, Quản lý; 31 form) |
| Mục tiêu | **Đủ giao diện và chức năng để làm báo cáo đồ án**: mọi form mở được trên dữ liệu mẫu, các luồng chính chạy thật, bảng chức năng có tình trạng |
| Hai máy | **MAC** — database, DTO, DAL, BLL, test, tài liệu. **WIN** — giao diện WinForms, chạy, in, chụp ảnh |
| Ngày lập | 27/10/2026 |

Mac đã kiểm: .NET SDK 10.0.400, Docker, chip Intel (SQL Server 2022 chạy trong Docker). Project WinForms **biên dịch** được trên Mac, nhưng **không chạy và không có Designer** — mọi việc cần nhìn giao diện thuộc WIN.

---

## 1. Quy tắc tránh xung đột

### 1.1. Mỗi thư mục một chủ

| Đường dẫn | Chủ | Máy kia |
|---|---|---|
| `HomestayDesktop.sln`, 5 `.csproj` không phải GUI | MAC (tạo ở M1.3) | WIN chỉ thêm project GUI vào `.sln` ở W1.1, ngoài ra không sửa |
| `.gitattributes`, `.gitignore` | MAC (đã có sẵn) | Không ghi đè khi VS đề nghị tạo `.gitignore` |
| `database/` | MAC | Chỉ chạy script, không sửa |
| `src/Homestay.DTO/`, `src/Homestay.DAL/`, `src/Homestay.BLL/` | MAC | Không sửa; cần thêm trường/hàm → tạo GitHub Issue nhãn `contract` |
| `tests/` | MAC | Không sửa |
| `src/Homestay.GUI/` (kể cả `Homestay.GUI.csproj`, `AppServices`, `appsettings.json`, `FileLogger`) | WIN | Không sửa; MAC chỉ build kiểm tra |
| `docs/screenshots/` | WIN | |
| `docs/` còn lại (tài liệu báo cáo) | MAC | |
| `HOMESTAY_DESKTOP_SPEC.md` | Chỉ sửa khi đã thống nhất; MAC sửa | |
| `WORK_PLAN.md` (file này) | Cả hai, **chỉ đánh dấu ô việc của mình** | |

### 1.2. Hợp đồng DTO + service

- Đầu mỗi đợt, MAC push **hợp đồng** (việc có dấu ⛳): DTO và chữ ký hàm service của đợt, commit bắt đầu bằng `contract(dotN):`. Hàm **đọc** chạy thật trên dữ liệu mẫu ngay; hàm **ghi** chưa xong thì `throw new NotImplementedException()`.
- WIN được **vẽ bố cục form** (kéo thả trong Designer, không cần DTO) bất cứ lúc nào; chỉ phần **nối dữ liệu và nút bấm** mới chờ hợp đồng của đợt đó lên `dev`.
- Trong một đợt, MAC **chỉ thêm**, không đổi tên/xóa/sửa chữ ký đã công bố. Bắt buộc đổi → ghi rõ trong commit `contract(dotN)!:` và báo WIN.
- WIN bắt `NotImplementedException` trong bộ bắt lỗi chung → hiện "Chức năng đang làm", không văng ứng dụng.
- Service mới của đợt: WIN tự thêm vào `AppServices` (thư mục GUI).

### 1.3. Database

- Toàn bộ bảng có từ đợt 1 (spec mục 11). Chỉ MAC sửa script; mỗi lần sửa chạy lại `database/build_install.sh` để sinh `install_demo.sql`, commit bắt đầu bằng `db:`.
- Thấy commit `db:` mới → WIN chạy lại `install_demo.sql` (tạo lại database, mất dữ liệu thử trên máy WIN — chấp nhận).
- Mật khẩu SQL của ứng dụng và mật khẩu tài khoản mẫu **không có trong Git**: truyền bằng biến SQLCMD `AppSqlPassword`, `FirstManagerPassword` (Quản lý đầu tiên, seed 010), `DemoPassword` (tài khoản mẫu, seed 020) khi chạy script. Seed băm bằng PBKDF2 1.000 vòng trong T-SQL; BLL băm lại 600.000 vòng ở lần đăng nhập đầu (spec 3.10).

### 1.4. Git

- Một nhánh làm việc chung `dev` (hai máy không sửa trùng thư mục nên không cần nhánh riêng). Cuối mỗi đợt MAC merge `dev` → `main` và gắn tag `dot1`, `dot2`…
- Commit nhỏ; **`git pull --rebase` trước khi push**.
- MAC chạy `dotnet build` cả solution (gồm GUI) trước khi push; không push code làm hỏng build GUI.
- Không commit `connection.dat`, mật khẩu, số tài khoản thật (repo public).

### 1.5. Đọc thứ tự

- Việc trong một máy làm **từ trên xuống**.
- ⛳ = mốc đồng bộ: việc của máy kia ghi "sau X" chỉ bắt đầu khi X đã push.
- MAC được làm trước tối đa **một đợt** (ví dụ làm M3.x khi WIN còn ở W2.x) vì không đụng thư mục GUI — để khi WIN chuyển đợt thì hợp đồng đã có sẵn, không phải chờ.

---

## 2. Môi trường

**MAC**

```bash
# SQL Server trong Docker; mật khẩu sa đặt qua biến môi trường, không ghi vào repo
# KHÔNG đặt MSSQL_COLLATION: collation server Vietnamese_CI_AS làm sp_getapplock hỏng trên Linux (xp_userlock);
# database vẫn là Vietnamese_CI_AS vì install_demo.sql tự đặt. Chỉ mở cổng ở 127.0.0.1.
docker run -d --name homestay-desktop-sql -p 127.0.0.1:14330:1433 \
  -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$SA_PASSWORD" \
  mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04
dotnet build HomestayDesktop.sln     # GUI có EnableWindowsTargeting nên build được trên Mac
HOMESTAY_TEST_CONNECTION="Server=127.0.0.1,14330;Database=HomestayDesktop_Dev;User ID=homestay_app;Password=…;Encrypt=True;TrustServerCertificate=True" \
  dotnet test                        # thiếu biến này thì test SQL tự Skip
```

**WIN**

1. Visual Studio 2026, workload ".NET desktop development".
2. SQL Server 2022 Developer hoặc Express **bản cập nhật (CU) mới nhất**, collation server để mặc định (database tự được tạo `Vietnamese_CI_AS` bởi script), bật TCP/IP; SQL Server Management Studio.
3. Mở `database/install_demo.sql` trong SSMS, bật **Query → SQLCMD Mode**, đặt `:setvar DbName HomestayDesktop`, `:setvar AppSqlPassword …`, `:setvar FirstManagerPassword …`, `:setvar DemoPassword …`, chạy.
4. Chạy ứng dụng lần đầu → màn F04 Cấu hình kết nối → lưu `connection.dat` (DPAPI). Môi trường phát triển được `TrustServerCertificate=true`.

---

## 3. Thứ tự công việc

### Đợt 1 — Nền tảng (spec G0)

Mục tiêu: đăng nhập được bằng tài khoản mẫu của 3 vai trò, menu đúng vai trò, quản lý tài khoản nhân viên và danh mục.

**WIN**

- [ ] **W1.0** Cài môi trường (mục 2) — làm ngay, không phụ thuộc.
- [ ] **W1.1** ⛳ (sau M1.3 bước tạo solution) **Thêm project GUI trong Visual Studio 2026** vào `HomestayDesktop.sln` có sẵn (MAC đã tạo DTO, DAL, BLL và 2 project test):
  - Mở `HomestayDesktop.sln` → Add → New Project → mẫu **Windows Forms App**, tên `Homestay.GUI`, đặt tại `src/Homestay.GUI`, `net10.0-windows`.
  - Thêm `<EnableWindowsTargeting>true</EnableWindowsTargeting>` vào `Homestay.GUI.csproj` để MAC build được.
  - Tham chiếu GUI → BLL, DTO (**không** tham chiếu DAL). Không thêm gói NuGet.
  - Giữ `Form1` tạm để chạy thử. Giữ nguyên `.gitignore`, `.gitattributes` có sẵn.
  - Xong khi: VS build xanh, ứng dụng mở được `Form1`; push lên `dev`; MAC chạy `dotnet build` xanh trên Mac.
- [ ] **W1.2** (sau W1.1) `Program.cs`: `AppServices`, đọc `appsettings.json`, bắt lỗi toàn cục (gồm `NotImplementedException` → "Chức năng đang làm"), `FileLogger`, kiểm `SchemaVersions` lúc khởi động. **F04** Cấu hình kết nối (DPAPI).
- [x] **W1.3** (sau M1.3) Control dùng chung trong `Common/`: lưới có phân trang 10/20/50/100 + **Hiện cột** + **Xuất CSV** UTF-8 BOM; ô chọn có tìm kiếm không dấu; định dạng tiền `1.234.000 ₫`, ngày `dd/MM/yyyy`; hộp thoại lỗi; nút "?"; `AsyncLoader` (bỏ kết quả tải cũ).
- [ ] **W1.4** (vẽ form sau W1.1; nối dữ liệu sau M1.1, M1.4) **F01** Đăng nhập, **F02** Đổi mật khẩu, **F03** Cửa sổ chính: menu 3 vai trò theo `Authorizer.Can`, tab (mở lại tab đang có), thanh trạng thái; mục menu của đợt sau tạm ẩn qua **một danh sách duy nhất**. Xóa `Form1`.
- [ ] **W1.5** (vẽ form sau W1.1; nối dữ liệu sau M1.4) **F18** tab Nhân viên, **F24** Danh mục & cấu hình.

**MAC**

- [x] **M1.1** ⛳ (làm ngay, chỉ đụng `database/`) `001_schema.sql` (mọi bảng mục 6.3 trừ module H), `002_indexes.sql`, `003_views.sql`, `004_security.sql`, `build_install.sh` → `install_demo.sql`. Xong khi: chạy sạch trên database trống, chạy lại không lỗi.
- [x] **M1.2** ⛳ `010_seed_reference.sql`, `020_seed_demo.sql` theo **mục 4** (dữ liệu đủ cho mọi màn, kể cả màn làm ở đợt sau).
- [x] **M1.3** ⛳ (không chờ WIN) Tạo `HomestayDesktop.sln` + `src/Homestay.DTO|DAL|BLL` (Class Library `net10.0`) + `tests/Homestay.Rules.Tests`, `tests/Homestay.BLL.Tests` (xUnit), tham chiếu đúng spec 3.2, gói `Microsoft.Data.SqlClient` 6.x vào DAL. Sau đó DTO chung `PagedResult<T>`, `PageRequest`, `Lookup` (push riêng trước để WIN làm W1.3); hạ tầng DAL `ConnectionFactory`, `UnitOfWork`, `DbClock`, `SqlErrorTranslator`, hàm đọc dòng dùng chung; BLL các exception, `ICurrentSession`, enum `Permission` đủ mục 5.2, `Authorizer`, `DataScope`, `AuditWriter`, `SettingsProvider`; `PasswordHasher` + test.
- [x] **M1.4** ⛳ Hợp đồng + cài đặt: `AuthService`, `SessionService` (đăng nhập, khóa tạm 5 lần sai, đổi mật khẩu bắt buộc, một phiên mỗi tài khoản, kiểm phiên trước thao tác ghi), `StaffService` (tạo nhân viên, khóa/mở, đặt lại mật khẩu), `CatalogService` (khu vực, tiện nghi, danh mục phòng, ngày lễ, chính sách hủy chung, tham số).

Xong đợt khi: trên máy WIN đăng nhập được NV/QL/KT mẫu; sai 5 lần bị khóa; QL tạo nhân viên mới, nhân viên đổi mật khẩu tạm; QL sửa danh mục và tham số.

### Đợt 2 — Luồng nhân viên (spec G2)

Mục tiêu: tra phòng → tạo đơn → ghi nhận thu cọc chạy trọn. Đây là phần lớn nhất của chương 2.

**MAC**

- [x] **M2.1** ⛳ Hợp đồng: `AvailabilityService` (tra phòng trống, lưới lịch phòng), `CustomerService`, `BookingService` (báo giá, tạo đơn, danh sách theo tab hàng đợi, chi tiết, nhận phụ trách, giao lại, hủy đơn chưa có tiền, tìm nhanh), `PaymentService.RecordReceipt`, `NotificationService` (số chưa đọc, danh sách, đánh dấu đã đọc), `FileService` (tải/mở ảnh chứng từ). Hàm đọc chạy thật trên seed.
- [x] **M2.2** `PricingCalculator`, chọn chính sách hủy + test (ngày thường/cuối tuần/lễ, đổi giá giữa kỳ ở, làm tròn cọc).
- [ ] **M2.3** Tạo đơn đầy đủ (khóa `RoomType:{id}`, `RoomNights`, `Counters`, giữ 15 phút), nhả giữ phòng quá hạn tại chỗ, `JobRunner` chỉ với tác vụ hết hạn giữ phòng; **test 10 luồng** cùng đặt.
- [ ] **M2.4** Ghi nhận thu cọc, nhận phụ trách/giao lại, hủy đơn chưa có tiền, thông báo; test khách thứ 4 đơn chờ bị chặn.

**WIN** (vẽ form ngay; nối dữ liệu sau M2.1)

- [ ] **W2.1** **F05** Tổng quan — thẻ của Nhân viên (thẻ QL/KT làm ở đợt 5, 6).
- [ ] **W2.2** **F06** tab Tra phòng, tab Lịch (khóa phòng làm ở đợt 4).
- [ ] **W2.3** **F13** Khách hàng.
- [ ] **W2.4** **F07** Tạo đơn (chưa có chọn voucher); sau khi tạo hiện mã đơn, số tiền, thông tin chuyển khoản, đồng hồ 15 phút.
- [ ] **W2.5** **F08** Danh sách đơn (các tab của Nhân viên), **F09** Chi tiết đơn (tab Thông tin, Giá, Thu chi, Lịch sử), **F10** Ghi nhận thu tiền.
- [ ] **W2.6** F03: ô tìm nhanh `Ctrl+K`, chuông và khung thông báo.

Xong đợt khi: từ S02 bấm Đặt → tạo đơn → ghi nhận thu → đơn "Chờ xác minh cọc"; đơn không ghi nhận quá 15 phút thành "Hết hạn" và phòng trống lại.

### Đợt 3 — Vòng đời đơn (spec G3)

Mục tiêu: một đơn đi trọn tạo → cọc → xác minh → xác nhận → nhận phòng → hoàn thành, cộng các nhánh từ chối/hủy/không đến có hoàn tiền.

**MAC**

- [ ] **M3.1** ⛳ Hợp đồng: `PaymentService` (hàng chờ, xác minh, từ chối, ghi nhận đã chi hoàn), `BookingService` (xác nhận/từ chối thay chủ, đổi phòng cùng loại, nhận phòng, trả phòng, không đến, danh sách đến/đi hôm nay, QL hủy do homestay không phục vụ được), `ChangeRequestService` **chỉ loại Hủy** (báo trước tiền hoàn, gửi, duyệt, từ chối).
- [ ] **M3.2** `BookingStateMachine`, `RefundCalculator` + test bảng chuyển trạng thái và bảng ví dụ mục 7.6.3.
- [ ] **M3.3** Cài đặt đủ M3.1; test: người ghi nhận không tự xác minh, hai người cùng xác nhận một đơn → một thành công, hai kế toán cùng chi hoàn → một thành công.

**WIN** (vẽ form ngay; nối dữ liệu sau M3.1)

- [ ] **W3.1** **F27** Xác minh thu cọc (trái hàng chờ, phải ảnh chứng từ + thông tin đơn).
- [ ] **W3.2** F09 các nút theo trạng thái: xác nhận/từ chối thay chủ (ghi chú bắt buộc), đổi phòng, nhận/trả phòng, không đến; F08 tab Chờ chủ xác nhận (đếm ngược, quá hạn tô đỏ) và tab Nhận/trả hôm nay.
- [ ] **W3.3** **F11** Yêu cầu hủy (chỉ loại Hủy), **F20** Duyệt hủy, F09 tab Đổi/hủy; nút QL "Hủy — homestay không phục vụ được".
- [ ] **W3.4** **F29** tab Hoàn tiền.

### Đợt 4 — Hồ sơ homestay (spec G1 + G4)

Đặt sau luồng nhân viên vì dữ liệu mẫu đã có homestay đang bán; giúp có ảnh chương 2 sớm.

**MAC**

- [ ] **M4.1** ⛳ Hợp đồng: `OwnerService`, `HomestayService` (danh sách, hồ sơ, tạo nháp, sửa, tiện nghi, ảnh, giấy tờ, gửi duyệt, danh sách điều kiện, duyệt/trả về/tạm ngưng/mở lại/kết thúc), `RoomService` (loại phòng, phòng, phiên bản giá, khóa/mở khóa phòng), `ContractService`, chính sách hủy riêng.
- [ ] **M4.2** Cài đặt + test: duyệt thiếu điều kiện hiện đúng danh sách thiếu; không có hợp đồng `Active` thứ hai; giá đã hiệu lực không sửa được; hoa hồng ngoài 10–15% bị chặn; NV không sửa homestay khác `Draft`; QL không duyệt nháp của mình; khóa trùng đơn bị chặn kèm mã đơn.

**WIN** (vẽ form ngay; nối dữ liệu sau M4.1)

- [ ] **W4.1** **F22** Chủ homestay.
- [ ] **W4.2** **F15** Danh sách homestay (tab Chờ duyệt cho QL).
- [ ] **W4.3** **F16** Hồ sơ homestay (tab Thông tin, Tiện nghi, Ảnh, Giấy tờ, Loại phòng & phòng, Bảng giá, Chính sách hủy, Hợp đồng, Lịch sử) + nút duyệt.
- [ ] **W4.4** **F23** Hợp đồng.
- [ ] **W4.5** F06 tab Lịch: khóa/mở khóa phòng (QL).

### Đợt 5 — Quản lý (spec G5)

**MAC**

- [ ] **M5.1** ⛳ Hợp đồng + cài đặt: phạm vi khu vực đầy đủ trong `DataScope`, giao nhiều đơn, `TaskService`, danh sách ngoại lệ (M02), phiên đăng nhập + buộc đăng xuất, đổi chức vụ/nghỉ việc, số liệu tổng quan QL; test NV ngoài phạm vi gọi BLL → "Không tìm thấy", hai người cùng nhận một đơn → một thành công.

**WIN** (vẽ form ngay; nối dữ liệu sau M5.1)

- [ ] **W5.1** F05 thẻ Quản lý; F08 tab ngoại lệ + chọn nhiều đơn để giao.
- [ ] **W5.2** F18 tab Khu vực, tab Phiên đăng nhập, đổi chức vụ/nghỉ việc.
- [ ] **W5.3** **F19** Giao việc; **F14** tab Việc được giao.

### Đợt 6 — Kế toán (spec G6)

**MAC**

- [ ] **M6.1** ⛳ Hợp đồng: sao kê (nhập tay, tự khớp, khớp tay, ghi đã hoàn trả, bỏ qua), `SettlementService` (lập/lập lại nháp, gửi, mở lại, ghi đã trả, ghi phản hồi của chủ), sổ thu chi, công nợ, số liệu tổng quan KT, dữ liệu cho bản in (gồm số tiền bằng chữ).
- [ ] **M6.2** `SettlementCalculator` + test công thức 7.6.6; test nhập trùng giao dịch, hai kế toán cùng lập một kỳ → một bảng, gửi bị chặn khi còn đơn chưa chốt, số tiền bằng chữ.

**WIN** (vẽ form ngay; nối dữ liệu sau M6.1)

- [ ] **W6.1** F05 thẻ Kế toán.
- [ ] **W6.2** **F28** Sao kê ngân hàng (nhập tay).
- [ ] **W6.3** **F30** Danh sách đối soát, **F31** Chi tiết đối soát.
- [ ] **W6.4** **F32** Sổ thu chi & công nợ.
- [ ] **W6.5** Bản in `PrintDocument`: phiếu thu/chi, bảng đối soát.

### Đợt 7 — Báo cáo và hoàn thiện (spec G7)

**MAC**

- [ ] **M7.1** ⛳ Hợp đồng + cài đặt: `ReportService` (Q15, K10, 7.6.8, dùng chung lớp công thức), tra `AuditLogs` (M16, S13), hiệu suất của tôi.
- [ ] **M7.2** Bổ sung dữ liệu mẫu nhiều tháng cho báo cáo có số liệu; test số liệu báo cáo khớp truy vấn đối chiếu viết tay.

**WIN** (vẽ form ngay; nối dữ liệu sau M7.1)

- [ ] **W7.1** **F25** Báo cáo (tab QL, tab KT).
- [ ] **W7.2** **F26** Nhật ký hệ thống; F14 tab Lịch sử thao tác, tab Hiệu suất.
- [ ] **W7.3** Hoàn thiện: phím tắt mục 8.1, khóa màn hình sau 30 phút (dùng lại F01), lưu "Hiện cột" theo người dùng, nội dung nút "?", kiểm DPI 100/125/150% và 1366×768.

### Đợt 8 — Tư liệu báo cáo (spec mục 14)

**MAC**

- [ ] **M8.1** Chương 1: ERD, danh sách bảng, ràng buộc chống trùng, ma trận quyền rút gọn, bảng tổng hợp chức năng có tình trạng (lấy từ mục 6 file này).
- [ ] **M8.2** Chương 3: bảng kết quả chạy thử (từ kết quả `dotnet test` + kiểm tay của WIN).

**WIN**

- [ ] **W8.1** Mở toàn bộ form với từng vai trò trên dữ liệu mẫu; chụp ảnh vào `docs/screenshots/Fxx_<vaitro>_<buoc>.png` theo thứ tự chương 2: đăng nhập → nhân viên → quản lý → kế toán; mỗi luồng mục 9 một chuỗi ảnh.
- [ ] **W8.2** Ảnh cây thư mục solution trong VS; ảnh menu từng vai trò; ghi kết quả kiểm tay (đăng nhập sai, phân quyền, hiển thị 150%) cho M8.2.

---

## 4. Dữ liệu mẫu bắt buộc (M1.2)

Seed đủ để **mọi màn có dữ liệu ngay**, kể cả khi nghiệp vụ ghi của màn đó chưa làm.

| Nhóm | Tối thiểu |
|---|---|
| Tài khoản | 3 Nhân viên (1 chưa gán khu vực), 2 Quản lý (1 `HasAllAreas`), 2 Kế toán; 1 tài khoản bị khóa; mật khẩu lấy từ biến `DemoPassword` |
| Danh mục | 3 khu vực (Tây Hồ, Ba Đình, Sóc Sơn), 8 tiện nghi, 5 danh mục phòng, ngày lễ năm 2026–2027, chính sách hủy chung `All` + `Holiday` |
| Chủ, homestay | 4 chủ (1 `Pending`); 6 homestay: 4 `Active`, 1 `Draft`, 1 `PendingReview`; mỗi homestay đang bán có hợp đồng `Active`, ảnh bìa, giấy tờ, 2–3 loại phòng × 2–4 phòng, đủ ba mức giá (có một phiên bản giá tương lai); 1 chính sách hủy riêng; 2 khóa phòng (kênh khác, bảo trì) |
| Khách | 15 khách, 1 có CCCD |
| Đơn | Mỗi trạng thái của mục 7.2 ít nhất 2 đơn, rải trong 2 tháng quanh ngày chạy; 1 đơn `Deposited` quá hạn xác nhận; 2 đơn nhận phòng hôm nay, 1 đơn trả phòng hôm nay; 3 đơn chưa giao |
| Tiền | Phiếu thu `Pending`/`Verified`/`Rejected`, tiền mặt + chuyển khoản; phiếu chi hoàn `Pending`/`Verified`; 1 yêu cầu hủy `Pending` |
| Sao kê | Đủ các trạng thái `Matched`, `Underpaid`, `LatePayment`, `Unmatched`, `Ignored` |
| Đối soát | 1 bảng `Draft`, 1 `Sent`, 1 `Paid` |
| Vận hành | 5 việc giao (1 quá hạn), thông báo chưa đọc cho mỗi tài khoản mẫu, AuditLogs cho các đơn mẫu |

---

## 5. Tạm chưa làm

Không làm trong kế hoạch này. Giao diện xử lý như cột cuối; bảng chức năng của báo cáo ghi "Chưa làm".

| Hạng mục | Spec | Xử lý tạm |
|---|---|---|
| Đổi ngày / đổi loại phòng | G8, 7.6.4 | F11, F20 chỉ có loại "Hủy". Khách muốn đổi → hủy theo chính sách rồi đặt đơn mới |
| Trùng phòng ba phương án, khoản bù | G8, 7.6.5 | F12 ẩn; tab Khoản bù của F29 ẩn. QL hủy với lý do "Homestay không phục vụ được", hoàn 100% (làm ở đợt 3) |
| Voucher xin lỗi | G8, 7.6.2 | F21 ẩn; F07 không có ô chọn voucher. Bảng vẫn có trong database |
| Cọc bổ sung | G8 | Tab Cọc bổ sung của F29 ẩn |
| Nhập tệp sao kê CSV/XLSX | Q12 còn mở | F28 chỉ nhập tay; làm khi có tệp mẫu MB Bank |
| Tác vụ định kỳ ngoài hết hạn giữ phòng | 3.11 | Danh sách quá hạn tính bằng truy vấn khi mở màn; KT tự bấm lập nháp đối soát; không gửi thông báo cảnh báo tự động |
| Kiểm thử nhiều máy, hiệu năng dữ liệu lớn, kiểm thử tự động bấm mọi nút | G9 | Sau báo cáo |
| Đóng gói self-contained, hướng dẫn cài, sao lưu/khôi phục | G9 | Sau báo cáo |

Đã bỏ hẳn theo spec (không phải "tạm"): vai trò Chủ homestay và F17 (D9), dịch vụ phát sinh (Q08).

---

## 6. Theo dõi form

| Form | Đợt | Việc WIN | Tình trạng |
|---|---|---|---|
| F01 Đăng nhập | 1 | W1.4 | Chưa làm |
| F02 Đổi mật khẩu | 1 | W1.4 | Chưa làm |
| F03 Cửa sổ chính | 1, 2 | W1.4, W2.6 | Chưa làm |
| F04 Cấu hình kết nối | 1 | W1.2 | Chưa làm |
| F05 Tổng quan | 2, 5, 6 | W2.1, W5.1, W6.1 | Chưa làm |
| F06 Tra phòng & lịch phòng | 2, 4 | W2.2, W4.5 | Chưa làm |
| F07 Tạo đơn | 2 | W2.4 | Chưa làm |
| F08 Danh sách đơn | 2, 3, 5 | W2.5, W3.2, W5.1 | Chưa làm |
| F09 Chi tiết đơn | 2, 3 | W2.5, W3.2, W3.3 | Chưa làm |
| F10 Ghi nhận thu tiền | 2 | W2.5 | Chưa làm |
| F11 Yêu cầu đổi/hủy | 3 | W3.3 (chỉ hủy) | Chưa làm |
| F12 Xử lý trùng phòng | — | — | Tạm chưa làm |
| F13 Khách hàng | 2 | W2.3 | Chưa làm |
| F14 Của tôi | 5, 7 | W5.3, W7.2 | Chưa làm |
| F15 Danh sách homestay | 4 | W4.2 | Chưa làm |
| F16 Hồ sơ homestay | 4 | W4.3 | Chưa làm |
| F17 | — | — | Bỏ (D9) |
| F18 Nhân viên & tài khoản | 1, 5 | W1.5, W5.2 | Chưa làm |
| F19 Giao việc | 5 | W5.3 | Chưa làm |
| F20 Duyệt đổi/hủy | 3 | W3.3 (chỉ hủy) | Chưa làm |
| F21 Voucher | — | — | Tạm chưa làm |
| F22 Chủ homestay | 4 | W4.1 | Chưa làm |
| F23 Hợp đồng | 4 | W4.4 | Chưa làm |
| F24 Danh mục & cấu hình | 1 | W1.5 | Chưa làm |
| F25 Báo cáo | 7 | W7.1 | Chưa làm |
| F26 Nhật ký hệ thống | 7 | W7.2 | Chưa làm |
| F27 Xác minh thu cọc | 3 | W3.1 | Chưa làm |
| F28 Sao kê ngân hàng | 6 | W6.2 (nhập tay) | Chưa làm |
| F29 Hàng chờ chi & cọc bổ sung | 3 | W3.4 (tab Hoàn tiền) | Chưa làm |
| F30 Danh sách đối soát | 6 | W6.3 | Chưa làm |
| F31 Chi tiết đối soát | 6 | W6.3 | Chưa làm |
| F32 Sổ thu chi & công nợ | 6 | W6.4 | Chưa làm |

Tình trạng dùng đúng ba nhãn của báo cáo: **Hoàn thành / Hoàn thành một phần / Chưa làm** (cộng "Tạm chưa làm", "Bỏ" cho hai loại ở mục 5).
