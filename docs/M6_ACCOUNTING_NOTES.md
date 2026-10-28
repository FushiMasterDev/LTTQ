# M6 — Đợt 6: Kế toán, bổ sung phần Mac

## Phạm vi và file

Giữ code M6 hiện hữu, chữ ký hàm đang được GUI gọi và toàn bộ DTO public. Chỉ sửa hai lỗi mục C; không viết lại nghiệp vụ. Không sửa GUI, database, spec, WORK_PLAN, FEATURE_STATUS, sơ đồ, solution/project; không NuGet, không commit. `docs/FEATURE_STATUS.md` và `docs/so-do/` đã untracked từ trước lượt này, giữ nguyên.

File sửa:

- `src/Homestay.BLL/DichVu/DichVuKeToan.cs`: kiểm null yêu cầu hoàn; audit số tiền phiếu Pending trước/sau khi khớp sao kê.
- `src/Homestay.DAL/KhoDuLieu/KhoTaiChinh.cs`: thêm truy vấn `GetPendingDepositAmountForUpdateAsync` đọc Amount với UPDLOCK trong transaction hiện hữu. Không đổi chữ ký hàm cũ.

File thêm:

- `tests/Homestay.Rules.Tests/KiemThuCongThucDoiSoatM6.cs`: 32 ca công thức, số tính tay trong comment; giữ nguyên ví dụ Completed không hoàn của `SettlementCalculatorTests`.
- `tests/Homestay.Rules.Tests/KiemThuSoTienBangChuMaNganHangM6.cs`: 22 ca đọc tiền và trích mã sao kê.
- `tests/Homestay.BLL.Tests/KiemThuNhapLieuKeToanM6.cs`: 3 ca không cần SQL, gồm null và ưu tiên kiểm quyền.
- `tests/Homestay.BLL.Tests/KiemThuHopDongSqlKeToanM6.cs`: 3 SqlFact (audit khớp tay, audit tự khớp, kỳ mẫu trọn luồng).
- `docs/M6_ACCOUNTING_NOTES.md`: hợp đồng và tư liệu chương 3.

Không cần sửa `SettlementService`, các Rules hay `SettlementRepository`. Các ca bổ sung không chỉ ra sai khác công thức 7.6.6/chuẩn hóa mã 7.6.7 cần sửa code; kết quả thực thi test phải xem mục kiểm chứng cuối tài liệu, không suy từ việc build thành công.

## Hợp đồng hàm → form

Tham chiếu spec 5.2, 5.3, 6.3 F, 7.5–7.7, 9.8–9.9, 10.4, D9/D12. KT = Kế toán, QL = Quản lý, NV = Nhân viên. Tên DTO thuộc `Homestay.DTO.Finance`; tham số `CancellationToken` tùy chọn được lược khỏi bảng. Hàm async trả `Task<T>`; bảng ghi T.

Lỗi chung: B = `BusinessException`; F = `ForbiddenException` (“Bạn không có quyền thực hiện thao tác này”); N = `NotFoundException` (“Không tìm thấy dữ liệu”); C = `ConcurrencyException` (“Dữ liệu vừa được người khác thay đổi. Đã tải lại, vui lòng kiểm tra và thao tác lại”); S = `SessionExpiredException` (“Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.”). F/N/C/S đều kế thừa B, GUI cần xử lý lớp cụ thể trước B. Các thao tác ghi dùng WriteOperationRunner/WriteSessionValidator, kiểm lại phiên trong transaction; BLL dịch lỗi constraint qua BusinessErrorTranslator/ConstraintMessages.

| Hàm | Form | Quyền và vai trò | DTO/tham số vào → ra | Exception và câu tiếng Việt tiêu biểu |
|---|---|---|---|---|
| `AccountingService.GetAccountantDashboardAsync` | F05 thẻ KT | PaymentVerify, chỉ KT | không → AccountantDashboard | F/S; số đếm toàn hệ thống |
| `ListBankTransactionsAsync` | F28 | BankStatementManage, KT; QL đọc | fromUtc, toUtc, MatchStatus?, search? → IReadOnlyList<BankTransactionItem> | B: “Khoảng thời gian phải là UTC, tối đa 366 ngày.” |
| `RecordBankTransactionAsync` | F28 nhập tay | BankStatementManage + bắt buộc Position.Accountant | RecordBankTransactionRequest → int Id | ArgumentNullException; B: “Số tiền hoặc thời gian giao dịch không hợp lệ.”; “Mã giao dịch đã tồn tại trên tài khoản ngân hàng này. Vui lòng kiểm tra sao kê đã nhập.” |
| `AutoMatchAsync` | F28 | BankStatementManage + KT | fromUtc, toUtc → IReadOnlyList<BankTransactionItem> | B khoảng thời gian; F/S. B từng dòng được giữ lại để xử lý; C cũng kế thừa B nên được bỏ qua theo cùng cơ chế |
| `MatchBankTransactionAsync` | F28 khớp tay (có thể từ F27) | BankStatementManage + KT | MatchBankTransactionRequest → BankTransactionItem | C/N; B: “Giao dịch đã được xử lý hoặc không còn ở trạng thái chờ.”; “Chỉ ghép tiền vào đơn đang chờ thanh toán hoặc chờ xác minh.”; “Đơn đã hết thời gian giữ phòng. Hãy xử lý khoản chuyển muộn.”; “Đơn đã có khoản cọc được xác minh hoặc đang chờ xử lý.” |
| `IgnoreBankTransactionAsync` | F28 | BankStatementManage + KT | ProcessBankTransactionRequest → BankTransactionItem | C; B: “Chỉ được bỏ qua giao dịch chưa khớp.”; lý do bắt buộc |
| `RecordUnmatchedRefundAsync` | F28 | UnmatchedPaymentRefund, chỉ KT | RefundBankTransactionRequest → BankTransactionItem | ArgumentNullException(request); C; B: “Thông tin hoàn giao dịch không hợp lệ.”; “Giao dịch đã được hoàn trả.”; “Chỉ được hoàn giao dịch chưa khớp, thiếu tiền, chuyển muộn hoặc phần chuyển dư.”; “Thời điểm hoàn không thể ở tương lai.” |
| `ListCashBookAsync` | F32 thẻ Sổ thu chi | CashBookManage, KT; QL đọc | fromUtc, toUtc → IReadOnlyList<CashBookItem> | B khoảng thời gian UTC |
| `ListPayablesAsync` | F32 thẻ Công nợ | CashBookManage, KT; QL đọc | không → IReadOnlyList<PayableItem> | F/S; đọc vw_Payables |
| `SettlementService.ListAsync` | F30 | SettlementView, KT/QL | year=0, month=0, period=0 → IReadOnlyList<SettlementListItem> | B: “Bộ lọc kỳ đối soát không hợp lệ.”; 0 nghĩa là tất cả |
| `GetAsync` | F31 | SettlementView, KT/QL | id → SettlementDraftResult | N; B: “Mã đối soát không hợp lệ.” |
| `CreateOrRecreateDraftAsync` | F30/F31 | SettlementCreate, KT/QL | homestayId, year, month, period → SettlementDraftResult | B: “Kỳ đối soát không hợp lệ.”; “Kỳ chưa hết thời gian chờ đối soát.”; “Kỳ này không có đơn để đối soát.”; “Bảng đã gửi hoặc đã trả, không thể tính lại.”; C khi nháp bị thay đổi |
| `SendAsync` | F31 | SettlementManage + KT | SettlementReasonRequest → SettlementDraftResult | C/N; B: “Trạng thái đối soát đã thay đổi.”; “Còn đơn chưa chốt trong kỳ: {các mã}”; “Số liệu kỳ đã thay đổi, hãy lập lại bảng nháp trước khi gửi.” |
| `ReopenAsync` | F31 | SettlementManage + KT | SettlementReasonRequest (Reason bắt buộc) → SettlementDraftResult | C/N; B: “Trạng thái đối soát đã thay đổi.”; lý do bắt buộc |
| `RecordOwnerFeedbackAsync` | F31 | SettlementManage + KT | SettlementFeedbackRequest → SettlementDraftResult | C/N; B nếu phản hồi rỗng/quá 500 ký tự; không đổi trạng thái/mốc gửi/trả |
| `MarkPaidAsync` | F31 | SettlementManage + KT | RecordSettlementPaidRequest → SettlementDraftResult | C/N; B: “Chỉ ghi đã trả cho bảng đang Đã gửi.”; “Chủ homestay chưa có đủ thông tin ngân hàng đã xác minh.”; “Nhập mã giao dịch chuyển tiền cho chủ.” |
| `SettlementCalculator.Calculate` | dữ liệu F31 | hàm thuần, BLL gọi sau kiểm quyền | SettlementCalculationInput → SettlementLine? | ArgumentNullException; B: “Dữ liệu đơn dùng lập đối soát không hợp lệ.”; “Trạng thái đơn không hợp lệ.”; “Tiền hoàn vượt nghĩa vụ thu của đơn.” |
| `VietnameseAmountWords.Spell` | bản in F31/F32 | hàm thuần | decimal → string | ArgumentOutOfRangeException: “Số tiền phải là số nguyên VND không âm, tối đa 18 chữ số.” |
| `BankStatementRules.ExtractBookingCode` | F28 tự khớp | hàm thuần | string? → string? | Không mã/null/rỗng trả null, không exception nghiệp vụ |

NV không có quyền kế toán/đối soát/sổ thu chi. QL có quyền đọc các danh sách và lập/lập lại nháp; ghi sao kê và gửi/mở lại/trả/ghi phản hồi còn kiểm Position.Accountant, vì Authorizer.Require chấp nhận cả mức ReadOnly. Thông báo bổ sung: “Thao tác ghi sao kê chỉ dành cho Kế toán.” / “Thao tác ghi đối soát chỉ dành cho Kế toán.”. F30 trong bảng form spec ghi QL đọc nhưng ma trận 5.2 cho QL lập nháp; BLL theo ma trận, không đổi GUI lượt này.

Mọi yêu cầu cập nhật có RowVer phải gửi đủ 8 byte đã tải; sai định dạng → B “Thiếu phiên bản dữ liệu. Vui lòng tải lại rồi thao tác lại.”. Các DTO đối soát trả token mới sau mỗi ghi. Các thao tác khớp/bỏ qua/hoàn sao kê hiện trả bản ghi `with` còn **RowVer cũ**: phải tải lại danh sách trước thao tác tiếp (F28 đang Reload). Không đổi hợp đồng trả về trong lượt này.

`SettlementDraftResult`: Totals, Items, UnfinalizedBookingCodes, RowVer; thêm HomestayName, OwnerName, OwnerBankName/OwnerBankAccountNo/OwnerBankAccountName, OwnerFeedback, History. `SettlementLine` giữ CommissionRate snapshot đơn và CheckOut; dữ liệu số là decimal VND, không tính tiền bằng double. Không có DTO hay trạng thái chủ xác nhận.

## Sao kê — luồng 9.8 và MatchStatus

1. Nhập tay qua RecordBankTransactionAsync: Source=Manual, MatchStatus=Unmatched; số nguyên VND > 0, tối đa 18 chữ số; TransactionTimeUtc khác mặc định và Kind=Utc. Trim tài khoản ≤30, mã giao dịch ≤100 (bắt buộc); nội dung ≤500, tên người chuyển ≤100, tài khoản người chuyển ≤30 (tùy chọn), chặn ký tự điều khiển. Unique `(AccountNumber, BankReference)` chặn trùng; nhập tay trả B “đã tồn tại”, không âm thầm bỏ qua.
2. AutoMatch chỉ đọc các dòng Unmatched trong khoảng `[fromUtc,toUtc)`, tối đa 366 ngày. Trích mã thành FJ + 6 chữ số + dấu nối + 3 chữ số, chấp nhận chữ thường/không gạch nối/ký tự phân cách. Nhiều mã hợp lệ: lấy mã xuất hiện đầu tiên (spec chưa chốt cách chọn). Chữ cái xen giữa chuỗi số không bị xóa để ghép nhầm mã. Không mã/không tìm thấy đơn → vẫn Unmatched. Mỗi dòng khớp dùng một transaction riêng, lỗi nghiệp vụ không rollback các dòng trước.
3. Khớp tay gửi TransactionId, BookingId, RowVer; không bắt buộc nội dung sao kê chứa mã đơn đã chọn. Cả hai đường đều xác minh phiếu cọc Pending có sẵn hoặc tạo phiếu Deposit/Verified mới, gắn BankTransactionId, đơn → Deposited, đặt ConfirmDeadlineAt theo Booking.ConfirmMinutes.

| Điều kiện / hành động | MatchStatus sau | Tiền, bước tiếp |
|---|---|---|
| Đơn AwaitingPayment/PaymentSubmitted, Amount ≥ DepositAmount − DiscountAmount | Matched (tự động) / ManuallyMatched (tay) | Một phiếu Verified, đơn Deposited; cùng dòng không khớp lại |
| Amount < nghĩa vụ cọc | Underpaid | Chỉ liên kết đơn/trạng thái, chưa tạo/xác minh phiếu, chưa đổi đơn; liên hệ khách hoặc hoàn |
| Đủ tiền nhưng đơn Expired/Cancelled/**Rejected** | LatePayment | Chưa thu cọc cho đơn cũ; NV tạo đơn mới nếu còn phòng, KT khớp tay vào đơn mới hoặc hoàn |
| Không mã / mã không tồn tại | Unmatched | Chọn đơn khớp tay, hoàn, hoặc bỏ qua |
| Chuyển dư và khớp thành công | Matched / ManuallyMatched | Phiếu ghi toàn Amount trên sao kê; Note “Chuyển dư {số} VND; xử lý hoàn khi khách yêu cầu.”; vw_Payables có ExcessTransfer |
| Hoàn Unmatched/Underpaid/LatePayment | Refunded | Ghi RefundBankRef, RefundedAt, người/mốc xử lý; hoàn toàn bộ Amount một lần |
| Hoàn phần dư của Matched/ManuallyMatched | Giữ trạng thái khớp | Chỉ hoàn phần ExcessTransfer còn phải trả; giữ phiếu Verified, không hoàn lại cọc; lưu mốc/mã hoàn, xóa khỏi công nợ dư |
| Bỏ qua Unmatched, Note 1–500 ký tự | Ignored | Không sinh phiếu; Underpaid/LatePayment không bỏ qua trực tiếp |

Thứ tự kiểm thực tế: **thiếu tiền trước trạng thái đơn**, nên một đơn đã hết hạn nhưng số tiền thiếu vẫn nhận Underpaid. Rejected cũng nhận LatePayment dù 7.6.7 chỉ liệt kê Expired/Cancelled; đây là mở rộng hợp lý hiện có, không sửa. AwaitingPayment quá HoldExpiresAt nhưng chưa đổi Expired trả B yêu cầu xử lý chuyển muộn, không tự chuyển LatePayment. AutoMatch không thử lại Underpaid/LatePayment; KT dùng khớp tay với token mới. Chưa có nghiệp vụ cộng nhiều dòng chuyển thiếu thành một phiếu.

Hoàn giao dịch: mã hoàn Trim không rỗng, ≤100, không ký tự điều khiển; RefundedAtUtc Kind=Utc và không quá giờ SQL +5 phút. Note tùy chọn; không tự chuyển tiền ngân hàng. Hoàn lần hai bị chặn bằng RowVer/mốc RefundedAt. Audit có BankTransaction.Record, AutoMatch/ManualMatch, Underpaid, LatePayment, Excess, Refund, Ignore.

**C2 đã sửa:** audit AutoMatch/ManualMatch trên BankTransactions giữ Status, BookingId/BookingCode, Amount như cũ; bổ sung OldValues.PaymentId/PaymentAmount của phiếu Pending và NewValues.PaymentId/PaymentAmount mới bằng Amount sao kê. Truy vấn UPDLOCK giữ số tiền cũ đến hết transaction; phiếu không còn Pending/chưa gắn ngân hàng → C. Hai SqlFact kiểm cả số cũ và mới, ID phiếu giữ nguyên, chỉ một Deposit, Verified, cùng người ghi/xác minh, trạng thái đơn và công nợ dư. Với đường tạo mới, PaymentId ở audit này là null (không có phiếu Pending), Old PaymentAmount=null, New PaymentAmount=số sao kê.

## Tách nhiệm vụ — 5.3

Phiếu thu nhập thủ công không có chứng cứ sao kê: người ghi ≠ người xác minh. Khi khớp sao kê thành công (tự động **hoặc tay**), có `BankTransactionId`, nên KT được xác minh cả phiếu do chính mình ghi. CHECK thực tế:

```sql
CK_Payments_ActorSeparation:
NOT (Direction = 'In' AND Status = 'Verified'
     AND BankTransactionId IS NULL AND VerifiedByUserId = RecordedByUserId)
```

Không nới CHECK, không bỏ kiểm người của đường xác minh phiếu thường. Ngoại lệ tay có BankTransactionId là cách áp dụng theo chứng cứ ngân hàng của code, rộng hơn chữ “tự khớp” trong câu spec. Unique BankTransactionId bảo đảm tối đa một phiếu/dòng sao kê. Quy tắc người gửi yêu cầu đổi/hủy ≠ người duyệt ≠ người xác nhận tiền vẫn giữ riêng; không dùng ngoại lệ sao kê để bỏ kiểm các vai trò của yêu cầu đổi.

## Đối soát — luồng 9.9, D9/D12 và đồng thời

- Kỳ 1: CheckOut ngày 1–15; kỳ 2: 16–cuối tháng. Đọc **ngày VN** từ giờ SQL +7; chỉ lập khi ngày cuối kỳ + Settlement.GraceDays **<** hôm nay. Mặc định 2 ngày, khoảng 0–15; bằng hôm nay vẫn chặn. Year 2020–2100, Month 1–12, Period 1/2.
- Lập nháp chỉ đưa đơn cuối có tiền giữ vào Items; đơn AwaitingPayment/PaymentSubmitted/Deposited/Confirmed/CheckedIn không có dòng nhưng trả mã trong UnfinalizedBookingCodes. Loại toàn bộ đơn có ReplacedByBookingId, kể cả khỏi danh sách chặn gửi. Kỳ mới không có dòng không tạo bảng 0; nháp đã tồn tại có thể tính lại thành rỗng.
- `Draft → Sent → Paid`; `Sent → Draft` mở lại với lý do bắt buộc. Paid cuối. D9 bỏ trạng thái Confirmed và mọi bước chủ xác nhận trong ứng dụng; chủ nhận bản in/CSV ngoài hệ thống, KT ghi phản hồi điện thoại/Zalo. Chữ Confirmed còn trong một câu 7.6.6 là phần cũ; thực hiện theo D9 và CHECK ba trạng thái.
- Send kiểm lại **từng dòng** nguồn, không chỉ tập BookingId/tổng. Còn đơn chưa chốt: B nêu mã; nguồn thay đổi: phải lập lại. Khi Sent/Paid, không lập lại trực tiếp. Reopen giữ dòng cũ cho đến lúc lập lại; audit lưu lý do, phản hồi không bị mất. Gửi lại cập nhật người/mốc gửi; Reopen hiện giữ mốc Sent cũ, trạng thái Draft quyết định không còn công nợ.
- D12: lấy applock `Settlement:{HomestayId}`, cập nhật header có RowVer và Status=Draft rồi xóa/ghi lại toàn bộ SettlementItems trong cùng transaction. Role được DELETE SettlementItems; BLL chỉ xóa bảng Draft. Unique `(HomestayId,Year,Month,PeriodNo)` và `SettlementItems.BookingId` chặn cuối. Không xóa header, không tính lại Sent/Paid.
- Ghi phản hồi ghi OwnerFeedback gần nhất và audit đủ lịch sử, không chuyển trạng thái; hiện cho phép cả Draft/Sent/Paid, không chỉ Sent. Ghi Paid chỉ Sent; nếu PayoutAmount>0 cần đủ BankName/BankAccountNo/BankAccountName hiện tại của chủ và mã giao dịch Trim 1–100, không ký tự điều khiển. Code kiểm các trường ngân hàng có đủ, không tự xác minh lại hoặc kiểm cờ Approved tại lúc trả. Payout=0 không bắt buộc ngân hàng/mã giao dịch. Không tự chuyển tiền cho chủ.
- Mọi cập nhật dùng RowVer và trạng thái cũ (10.4), lỗi trả C, giao dịch rollback cả tiền/trạng thái/audit. Các kết quả đối soát sau ghi phải dùng RowVer mới. Lịch sử đọc AuditLogs, không tạo bước chủ xác nhận giả.

## Công thức 7.6.6

Các biến dưới đây là snapshot của **đơn**, không lấy lại tỷ lệ hợp đồng hiện tại. `AmountDue = DepositAmount − DiscountAmount`. `refund = RefundAmount ?? 0`. Round là decimal, 0 chữ số, MidpointRounding.AwayFromZero; RoundToThousand làm tròn nghìn cùng quy ước.

| Dòng công thức | Ý nghĩa / kiểm chứng |
|---|---|
| `p = AmountDue > 0 ? Round(100 × refund / AmountDue) : 0` | Tính tỷ lệ trước, không dùng trực tiếp tỷ lệ chưa làm tròn để phân bổ. Test 50,5% → 51%; p ngoài 0–100 trả B |
| `feeOnly = Status ∈ {Rejected, Expired}` | Không giữ phần cọc gốc; code thêm Cancelled chưa có cọc Verified, xem dưới |
| `baseRetained = feeOnly ? 0 : DepositAmount − RoundToThousand(DepositAmount × p / 100)` | Test giữa 300.500 → 301.000 và hai bên biên |
| `baseCommission = MIN(Round(TotalAmount × CommissionRate), baseRetained)` | Test hoa hồng vượt cọc giữ: chặn ở baseRetained, payout có thể bằng 0 |
| `feeCommission = MIN(Round(ChangeFeeAmount × CommissionRate), ChangeFeeAmount)` | Phí đổi có hoa hồng riêng; test 5 × 0,10 = 0,5 → 1 |
| `Retained = baseRetained + ChangeFeeAmount` | ≤0 không có dòng; kiểm cả 0 và âm do làm tròn |
| `Commission = baseCommission + feeCommission` | Tỷ lệ .10/.15 và biên .5 đồng đều có test |
| `Payout = Retained − Commission` | Số Fuji chuyển chủ, không trừ voucher lần nữa |
| `Discount(dòng) = feeOnly ? 0 : MAX(0, DiscountAmount − RoundToThousand(DiscountAmount × p / 100))` | Voucher do Fuji chịu; test tỷ lệ, biên nghìn và MAX chặn âm |
| `Fuji thực thu = Commission − Discount(dòng)` | Ví dụ Completed có voucher 50.000: Commission=240.000, thực thu=190.000, payout vẫn 360.000; DTO không có trường thực thu riêng |
| `DepositAmount(dòng) = DepositAmount + ChangeFeeAmount` | Tổng snapshot dùng giới hạn dòng; ngoại lệ Cancelled chưa có cọc Verified bên dưới |

**Cách hiểu ngoài spec giữ nguyên:** Cancelled **không có Deposit/In/Verified** được xem feeOnly: bỏ cọc danh nghĩa, Discount(dòng)=0, DepositAmount(dòng)=ChangeFeeAmount. Không phí → không dòng; phí 200.000 → retained 200.000, commission 24.000, payout 176.000. Spec nói “Cancelled có cọc giữ lại”, nhưng biểu thức feeOnly nguyên văn chỉ nêu Rejected/Expired; notes ghi riêng cách diễn giải này, test cố định cả hai nhánh. Completed/NoShow vẫn dùng snapshot cọc theo công thức, không thêm điều kiện Verified mới.

Đọc tiền: 0 → “Không đồng”; 1.000 → “Một nghìn đồng”; 10.000 → “Mười nghìn đồng”; 105.000 → “Một trăm lẻ năm nghìn đồng”; 1.000.005 → “Một triệu không trăm lẻ năm đồng”; 21.000.000 → “Hai mươi mốt triệu đồng”; 1.250.000.000 → “Một tỷ hai trăm năm mươi triệu đồng”. Kiểm cả 999.999.999.999.999.999 (tối đa hợp lệ), âm/lẻ đồng/vượt giới hạn. “Lẻ”, “mốt”, “lăm” tùy vị trí; code dùng “hai mươi **bốn**”, là biến thể chuẩn của “hai mươi **tư**”, không ép đổi Rules ngoài phạm vi. 14 vẫn “mười bốn”, 104 vẫn “một trăm lẻ bốn”.

## Kỳ đối soát mẫu

Tư liệu chương 3 nằm trong `SamplePeriodMatchesHandLedgerFromDraftThroughPaidAndBothFinanceViews`. Homestay riêng, hợp đồng 12%, chính sách riêng có mốc hoàn 50%; năm 2025, tháng 1, kỳ 1. Cả 5 đơn CheckIn=05/01, CheckOut=07/01 nên cùng kỳ theo CheckOut. Mã đơn sinh thật qua BLL, bảng dưới dùng nhãn để không phụ thuộc bộ đếm. Đơn Cancelled có mốc hủy 02/01, 3 ngày trước nhận phòng, refund snapshot 50%. Mỗi đơn: TotalAmount=2.000.000, DepositAmount=600.000, AmountDue=600.000, DiscountAmount=0, CommissionRate=0,12.

| Đơn | Refund | Phí đổi | p | Deposit(dòng) | Retained | Commission | Discount(dòng) | Payout |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Completed | 0 | 0 | 0% | 600.000 | 600.000 | 240.000 | 0 | 360.000 |
| NoShow | 0 | 0 | 0% | 600.000 | 600.000 | 240.000 | 0 | 360.000 |
| Cancelled giữ 50% | 300.000 | 0 | 50% | 600.000 | 300.000 | 240.000 | 0 | 60.000 |
| Rejected có phí đổi đã thu | 600.000 | 200.000 | 100% | 800.000 | 200.000 | 24.000 | 0 | 176.000 |
| CheckedIn, sau chốt Completed | 0 | 0 | 0% | 600.000 | 600.000 | 240.000 | 0 | 360.000 |

Rejected là feeOnly: baseRetained=0, baseCommission=0, feeCommission=200.000×12%=24.000. Cancelled: baseRetained=600.000−300.000=300.000; hoa hồng MIN(240.000,300.000)=240.000; payout=60.000.

| Bảng | Số dòng | TotalRoomAmount | TotalRetained | TotalCommission | TotalDiscount | PayoutAmount |
|---|---:|---:|---:|---:|---:|---:|
| Nháp đầu (đơn 5 còn CheckedIn) | 4 | 8.000.000 | 1.700.000 | 744.000 | 0 | 956.000 |
| Lập lại sau khi chốt đơn 5 | 5 | 10.000.000 | 2.300.000 | 984.000 | 0 | 1.316.000 |

Test so từng SettlementLine đầy đủ (loại dòng, mã/Id, ngày, tiền, tỷ lệ) và SettlementTotals bằng hằng số tính tay, không gọi Calculator để sinh expected. Gửi lần đầu trả đúng “Còn đơn chưa chốt trong kỳ: {mã đơn 5}”, header/token giữ nguyên. SQL chốt Completed → BLL lập lại cùng Id → gửi → ghi phản hồi chủ → mở lại với lý do → lập lại và gửi lại → ghi Paid với `M6-SAMPLE-PAYOUT`. Kiểm Paid không mở lại, giữ snapshot và lịch sử các thao tác.

**Nguồn dữ liệu dựng:** M21Data tạo nền homestay/phòng/chủ/chính sách/hợp đồng riêng bằng SQL, có 5 phòng. Test đặt giá cả ba loại ngày của riêng RoomType=1.000.000 để giá 2 đêm xác định; cấp thông tin ngân hàng giả và duyệt chủ bằng SQL. Từng đơn được CreateAsync qua BLL với phòng riêng; từng cọc 600.000 được RecordBankTransactionAsync và MatchBankTransactionAsync qua BLL, tạo Verified thật. Sau đó SQL đưa ngày/trạng thái và snapshot hoàn/phí về kỳ lịch sử; dịch cả BookingNights/RoomNights, giải phóng RoomNights đơn cuối không giữ phòng. SQL dựng yêu cầu đổi Applied/phiếu cọc bổ sung 200.000 và hai phiếu hoàn Verified 300.000/600.000; dùng ba tài khoản thật riêng cho người gửi/duyệt/xác nhận, không tắt constraint. Không tuyên bố các bước đổi/hủy lịch sử đã chạy qua BLL. Các mốc tài chính VerifiedAt là giờ SQL hiện tại để kiểm sổ theo ngày ghi tiền; kỳ đối soát vẫn theo CheckOut, không theo VerifiedAt.

**View kiểm trực tiếp và qua BLL:** khi Sent, vw_Payables có một OwnerPayout 1.316.000; vw_CashBook chưa có dòng Settlement. Khi mở lại, công nợ bảng biến mất; gửi lại tạo lại công nợ. Khi Paid, vw_CashBook có đúng một OwnerPayout với mã giao dịch, ExpenseAmount=1.316.000, IncomeAmount=0; vw_Payables không còn khoản nào của homestay mẫu. Tổng thu: 5×600.000+200.000=3.200.000. Tổng chi: 300.000+600.000+1.316.000=2.216.000. Chênh lệch=984.000=hoa hồng (không voucher). Kiểm SUM trực tiếp trên view theo HomestayId để không bị TOP của API danh sách cắt mất.

## Giới hạn giữ nguyên và phần chưa làm

| API / phần | Giới hạn thực tế | Đề xuất sau này, chưa thay hợp đồng GUI |
|---|---|---|
| ListBankTransactionsAsync | TOP(1000), TransactionTime DESC/Id DESC; AutoMatch cũng chỉ xử lý tối đa 1000 Unmatched của lần đọc | Thêm API phân trang riêng và tổng số; AutoMatch dùng lô với con trỏ để không bỏ sót dòng cũ, giữ API cũ tương thích |
| ListCashBookAsync | TOP(2000), EntryAt DESC/SourceType/SourceId DESC; tổng từ danh sách có thể thiếu | Phân trang và API SUM theo bộ lọc ở SQL; không dùng tổng trang làm tổng kỳ |
| ListPayablesAsync | **TOP(2000)**, chưa phân trang, PayableAt/SourceType/SourceId; cũng có nguy cơ thiếu | Thêm phân trang, bộ lọc và tổng SQL. Yêu cầu nhắc “không phân trang”; code thực tế còn có TOP 2000, không sửa bỏ TOP lượt này |
| GetAccountantDashboardAsync | Đếm trực tiếp toàn bộ bảng/view, không bị TOP list | Chênh lệch thẻ với danh sách bị cắt phải được GUI giải thích khi bổ sung phân trang |
| Hoàn phần chuyển dư | Giữ trạng thái Matched/ManuallyMatched; vw_Payables loại dư đã hoàn theo RefundedAt | vw_CashBook hiện chỉ nhận hoàn sao kê MatchStatus=Refunded nên **chưa có dòng chi phần dư đã hoàn**. Ghi nhận giới hạn để xử lý ở đợt database, không sửa view ngoài phạm vi |
| Tự khớp cọc bổ sung | Nhánh đề xuất 7.6.7 bước 5 chưa có trong AccountingService, hiện khớp cọc ban đầu theo nghĩa vụ đơn | Thiết kế cùng luồng ChangeRequest/PaymentService và quy tắc ba người; không suy diễn đã hỗ trợ từ việc vw_Payables hiểu AdditionalDeposit |
| Nhập tệp sao kê 9.8 bước 1–2 | Chưa có parser/ánh xạ cột/xem trước/lưu tệp StoredFiles, xử lý dòng mới/trùng/lỗi theo lô nhập | WORK_PLAN để sau, chờ tệp mẫu che thông tin theo Q12; lượt này không sửa WORK_PLAN và không thêm NuGet |

Không đưa số tài khoản ngân hàng thật vào tài liệu/test. Dữ liệu ngân hàng ở kỳ mẫu là giả. In/xuất CSV và gửi chủ ngoài ứng dụng thuộc GUI/quy trình vận hành; các service trên chỉ ghi nhận, không gửi email/Zalo hoặc chuyển khoản.

## Kiểm chứng trên Mac

Thêm **60 ca** (tính từng InlineData): **54 Rules + 6 BLL**, gồm **3 BLL không SQL và 3 SqlFact**. Mốc 1176 Rules + 225 BLL trên SQL thật do người giám sát cung cấp là kết quả trước lượt này, không phải kết quả chạy lại của 60 ca mới.

Lệnh chuẩn, không bộ chạy thay thế:

```sh
dotnet build HomestayDesktop.sln --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test HomestayDesktop.sln --no-build --no-restore -m:1 --logger 'console;verbosity=quiet'
```

Build cuối: **0 lỗi, 0 cảnh báo**. Lệnh test chuẩn hoàn tất exit code 0: **Rules 1230 đạt / 0 lỗi / 0 Skip**; **BLL 3 đạt / 0 lỗi / 228 Skip / tổng 231**. HOMESTAY_TEST_CONNECTION không được đặt nên SqlFact tự Skip; 3 BLL đạt là các ca hợp đồng đầu vào không dùng SQL mới thêm. **Chưa xác nhận thực thi 3 SqlFact mới trên SQL thật**, gồm kỳ mẫu và audit hai đường khớp. Người giám sát chạy lại trên database test SQL Server đã cài schema/seed hiện có bằng tài khoản test theo quy trình sẵn có; không cần migration mới. Không tạo runner riêng để vòng qua sandbox.

Hai lỗi đã sửa: thiếu ArgumentNullException của RecordUnmatchedRefundAsync; audit khớp phiếu Pending thiếu Amount cũ. Không thay hành vi khớp/ghi đè tiền, không sửa Calculator/BankStatementRules theo kết quả giả định.
