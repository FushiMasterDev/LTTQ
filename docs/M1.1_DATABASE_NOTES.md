# M1.1 — Ghi chú bàn giao database

Căn cứ: `HOMESTAY_DESKTOP_SPEC.md` v0.4 và `WORK_PLAN.md` M1.1. Chỉ tạo file trong `database/`, chưa làm seed, không có module H, không commit.

## Bộ script

- `001_schema.sql`: 38 bảng, 38 PK, 79 FK, 112 CHECK, 21 UNIQUE dạng constraint và 12 unique có lọc.
- `002_indexes.sql`: 22 chỉ mục hiệu năng còn lại của mục 6.4.
- `003_views.sql`: 3 view theo mục 6.5; không có `vw_RoomDayState`.
- `004_security.sql`: role, login, user và quyền tối thiểu; không cấp quyền DDL.
- `build_install.sh`: ghép theo thứ tự 001, 002, 003, 004, 010, 020; bỏ qua file chưa có. Hiện chưa có 010/020.
- `install_demo.sql`: đầu ra đã sinh bằng `bash database/build_install.sh`.

Khi chạy bộ cài trên SQL Server 2022, bật SQLCMD Mode và truyền `DbName` (thông thường `HomestayDesktop`), `AppSqlPassword` từ ngoài file. Script không đặt mặc định để không ghi đè biến người chạy truyền vào. Khi có seed M1.2, truyền thêm biến mà seed yêu cầu và chạy lại build. Bộ cài tạo lại database theo yêu cầu; chạy riêng 001–004 trên cùng database sẽ dùng phiên bản để bỏ qua script đã áp dụng.

## Danh sách bảng

| Nhóm | Bảng |
|---|---|
| A (5) | Users, Employees, EmployeeAreas, Owners, UserSessions |
| B (7) | Areas, Amenities, RoomCategories, Holidays, SystemSettings, Counters, SchemaVersions |
| C (7) | Homestays, HomestayAmenities, HomestayImages, VerificationDocuments, Contracts, CancellationPolicies, CancellationRules |
| D (5) | RoomTypes, Rooms, RoomTypePrices, RoomBlocks, RoomNights |
| E (6) | Customers, Bookings, BookingNights, BookingStatusHistories, BookingChangeRequests, Vouchers |
| F (4) | Payments, BankTransactions, Settlements, SettlementItems |
| G (4) | Notifications, WorkTasks, AuditLogs, StoredFiles |

## Những điểm chưa tường minh và lựa chọn đã dùng

1. **NULL, kiểu và DEFAULT bị lược trong bảng tóm tắt:** các thuộc tính nghiệp vụ chính, cờ, mã phân loại, số tiền và tên danh mục là `NOT NULL`; mô tả/ghi chú, JSON cũ/mới và trường đánh dấu tùy chọn là `NULL`. Không tự thêm DEFAULT trạng thái/cờ/số tiền nếu spec không ghi. `SchemaVersions.ScriptName`, `AppliedAt`, `BookingStatusHistories.ChangedAt` là `NOT NULL`; mọi thời điểm dùng `DATETIME2(3)`. Các FK người dùng bị lược kiểu là `INT`; `RevokedAt`, `ProcessedAt`, `RefundedAt`, `SentAt`, `PaidAt` là `DATETIME2(3) NULL`; `RevokeReason` là `NVARCHAR(500) NULL`, `RefundBankRef` là `VARCHAR(100) NULL`. `RoomCode` giữ `NVARCHAR(20)` theo định nghĩa riêng mục 6.3.D, dù quy ước chung nói mã dùng VARCHAR.

2. **Cột chung:** cả 38 bảng có `CreatedAt` và DEFAULT UTC. Các bảng không có `UpdatedAt`: SchemaVersions, EmployeeAreas, HomestayAmenities, RoomNights, BookingNights, BookingStatusHistories, AuditLogs, StoredFiles — đây là bảng ghi nhận phiên bản, quan hệ tạo/xóa, snapshot hoặc dữ liệu giữ nguyên sau khi thêm. Các bảng còn lại có `UpdatedAt DATETIME2(3) NULL`, không tự cập nhật bằng trigger. Chỉ 10 bảng có RowVer theo định nghĩa/mục 10.4: Users, Homestays, Customers, Bookings, BookingChangeRequests, Vouchers, Payments, BankTransactions, Settlements, WorkTasks. UserSessions có UpdatedAt theo cột chung, nhưng role ứng dụng chỉ được cập nhật đúng ba cột yêu cầu nên không ghi UpdatedAt.

3. **Unique có lọc của Owners:** yêu cầu giao việc nhắc Owners nhưng bảng v0.4 không ghi cột cần lọc; chọn `IdNumber IS NOT NULL` để CCCD đã nhập không trùng và nhiều chủ chưa nhập CCCD vẫn hợp lệ. Phone vẫn UNIQUE thông thường. Không khôi phục `Owners.UserId` đã bỏ ở D9.

4. **Một phiên mỗi tài khoản:** thêm `UX_UserSessions_OneActiveSession (UserId) WHERE EndedAt IS NULL` từ quy tắc mục 3.6/12.3. BLL phải kết thúc phiên cũ trước khi thêm phiên mới trong transaction. Chỉ mục kiểm phiên chưa kết thúc, không tự coi phiên mất heartbeat là đã kết thúc.

5. **Lý do bắt buộc và NULL:** diễn giải `Reason <> ''` thành `IS NOT NULL` và `LEN(LTRIM(RTRIM(...))) > 0`; chuỗi chỉ gồm dấu cách cũng bị chặn. CHECK của trường tùy chọn vẫn cho phép NULL khi không thuộc trạng thái yêu cầu. Những điều kiện spec ghi “bắt buộc khi” giữ theo chiều suy ra, không tự buộc xóa mốc cũ ở mọi trạng thái khác. Ví dụ Sent → Draft có thể giữ metadata lần gửi cũ. Riêng ConfirmedAt, cặp EndedAt/EndReason, trạng thái quyết định và VoucherId/DiscountAmount tuân theo các trường hợp hai chiều đã nêu.

6. **BookingChangeRequests:** “ba cột mới” được hiểu là hai ngày và NewRoomTypeId; bổ sung NewRoomId phải NULL với Cancel vì hủy không giữ phòng mới. Với các loại đổi, NewRoomId cho phép NULL trước lúc chọn/giữ phòng. Pending có cả DecidedByUserId/DecidedAt NULL, trạng thái khác có đủ cả hai. Cặp FinanceConfirmedByUserId/FinanceConfirmedAt phải cùng NULL hoặc cùng có giá trị; có người xác nhận tài chính thì phải có người quyết định, và ba người khác nhau. AdditionalDeposit vẫn NULL được khi spec không bắt buộc số tiền; chỉ Applied với AdditionalDeposit > 0 mới bắt buộc xác nhận tài chính.

7. **Nhất quán bổ sung từ diễn giải nghiệp vụ:** Owners Approved phải đủ ba trường ngân hàng theo ghi chú cột BankName. Vouchers Active phải Balance > 0, UsedUp phải Balance = 0 theo mục 7.5; Expired/Revoked có thể còn hoặc hết số dư. BookingStatusHistories kiểm danh sách FromStatus/ToStatus và ActorType=System ⇔ ActorUserId NULL. StoredFiles kiểm `DATALENGTH(Content) = SizeBytes` để giới hạn 5 MB áp dụng cho dữ liệu thật, không chỉ số khai báo.

8. **BankTransactions:** Matched/ManuallyMatched/Underpaid/LatePayment phải có MatchedBookingId theo ý nghĩa mục 7.5. ProcessedByUserId/ProcessedAt phải cùng NULL hoặc cùng có giá trị; ManuallyMatched/Refunded/Ignored cần đủ người và thời điểm xử lý vì là thao tác tay. Ignored cần Note không rỗng. Refunded cần RefundedAt/RefundBankRef. SourceFileId vẫn cho NULL theo định nghĩa, không tự bắt buộc khi Import.

9. **Hợp đồng và quy tắc BLL:** CHECK CommissionRate giữ `> 0 AND < 1` đúng bảng Contracts; giới hạn cấu hình 10–15%, điều kiện ngày thực tế, thứ tự thời gian, quan hệ phòng–loại phòng–homestay, tổng BookingNights, trạng thái liên bảng, chủ đủ ngân hàng khi trả đối soát, quyền/tách nhiệm vụ cần đọc bảng khác vẫn do BLL kiểm. Không dùng trigger. Không thêm các điều kiện mốc PaymentSubmittedAt/ConfirmDeadlineAt ngoài danh sách CHECK Bookings mà spec đã nêu.

10. **Hợp đồng đầu ra của view:** spec chưa định tên cột nên chọn các tên sau để DAL có thể dùng ổn định.
    - `vw_BookingPaymentState`: BookingId, BookingCode, BookingStatus, AmountDue, VerifiedDepositAmount, VerifiedAdditionalDepositAmount, VerifiedRefundAmount, PaymentState, PaymentStateName, RefundExtent. Ưu tiên: Refund Pending → Refund Verified → yêu cầu đổi AwaitingPayment → giữ cọc → Deposit Pending → Deposit Verified → Expired/Cancelled chưa thu → chưa thu cọc. PaymentState lần lượt là RefundPending, Refunded, AdditionalDepositPending, DepositRetained, DepositPending, DepositVerified, NoFinancialActivity, DepositNotCollected. RefundExtent là Full khi tổng hoàn đã xác minh ≥ AmountDue, Partial khi nhỏ hơn; NULL nếu chưa hoàn. Hoàn tính bằng số thực ghi nhận trên phiếu Verified, không dùng số hoàn dự kiến. Tổng hợp Payments và yêu cầu đổi riêng trước khi join để giữ đúng một dòng/đơn.
    - `vw_CashBook`: SourceType, SourceId, EntryAt, ReferenceNo, EntryType, IncomeAmount, ExpenseAmount, PartyName, RecordedByUserId, RecordedByUsername, BookingId, HomestayId, Note. Dùng UNION ALL vì mỗi dòng tiền là một chứng từ riêng. Ngày ghi sổ là VerifiedAt/PaidAt/RefundedAt. “Người ghi” là Payments.RecordedByUserId, Settlements.PaidByUserId hoặc BankTransactions.ProcessedByUserId. Tiền chuyển chủ không có số phiếu riêng nên dùng PaidBankRef hoặc mã tổng hợp SET-Id, không thêm cột mới vào bảng. BankTransactions Refunded ghi chi toàn bộ Amount theo định nghĩa hoàn dòng sao kê; không tự bù thêm dòng thu ngoài phạm vi view trong spec.
    - `vw_Payables`: SourceType, SourceId, PayableAt, ReferenceNo, PayableType, Amount, PartyName, BookingId, HomestayId, RecordedByUserId, Note. Ngày là SentAt/RecordedAt/TransactionTime; tiền là số còn phải trả của từng nguồn, không cộng gộp mất chứng từ.

11. **Công nợ chuyển dư chưa có cấu trúc riêng:** không suy đoán ý nghĩa bằng từ khóa tự do trong Note. Chọn dòng Matched/ManuallyMatched đã gắn phiếu thu Verified, Note không rỗng và Amount lớn hơn nghĩa vụ cọc. Nghĩa vụ là Bookings.DepositAmount − DiscountAmount với Deposit; BookingChangeRequests.AdditionalDeposit với cọc bổ sung. Công nợ dư = Amount sao kê − nghĩa vụ. Refunded/Ignored không vào nhánh này. Giới hạn dữ liệu hiện tại: spec không có số dư phải hoàn riêng, số đã hoàn dư từng phần hoặc snapshot nghĩa vụ cọc gốc trên sao kê. Sau khi áp dụng đổi đơn, nghĩa vụ hiện tại có thể khác lúc chuyển; trả dư một phần cũng chưa thể biểu diễn chính xác. View dùng được cho trường hợp đã khớp, cọc chưa đổi và chưa hoàn dư; BLL/giám sát phải đối chiếu thủ công những trường hợp còn lại. Không tự thêm bảng/cột để đổi thiết kế.

12. **Quyền và login:** cấp quyền tường minh từng bảng, không cấp rộng trên schema; cấp SELECT và DENY ghi trên ba view. DENY UPDATE mức bảng UserSessions rồi GRANT UPDATE đúng LastSeenAt, EndedAt, EndReason để khớp mục 3.9 và yêu cầu giao việc. Đây là ngoại lệ quyền theo cột được [Microsoft Learn mô tả](https://learn.microsoft.com/en-us/sql/t-sql/statements/grant-object-permissions-transact-sql?view=sql-server-ver16). Các bảng ngoài sáu bảng được xóa đều DENY DELETE; SchemaVersions chỉ SELECT. Login mới bật CHECK_POLICY, tắt CHECK_EXPIRATION để tài khoản ứng dụng không hết hạn định kỳ; login đã có không bị đổi mật khẩu hay cấu hình. Script dùng biến SQLCMD thay thế literal theo yêu cầu, không tạo hay lưu giá trị mật khẩu trong bộ cài. Không gán db_owner/db_datawriter hoặc quyền DDL.

13. **Chạy lại và transaction:** 001 tạo SchemaVersions nếu chưa có, trước khi đọc Version để không tham chiếu bảng chưa tồn tại. Sau guard NOEXEC, mỗi script có một transaction và chỉ ghi Version khi hết nội dung; tất cả FK của 001 được thêm cuối file để xử lý vòng phụ thuộc. Khởi tạo SchemaVersions nằm ngoài transaction 001 nên có thể còn lại bảng phiên bản trống nếu lần đầu thất bại. Điều này cho phép chạy lại sau khi transaction tạo bảng nghiệp vụ rollback. Bản thân guard chỉ bỏ qua phiên bản đã áp dụng, không tự sửa schema đã bị sửa thủ công.

## Kiểm tra đã thực hiện

- `bash -n database/build_install.sh` đạt; chạy build thật và chạy lại từ thư mục khác cho cùng nội dung SHA-256.
- Kiểm tra tĩnh tập bảng, tên PK/ràng buộc không trùng, các cột/kiểu FK khớp, đúng ba CASCADE, đủ RowVer, D9, quyền DELETE/cột, guard 1–4 và nội dung ghép.
- Đánh giá 69 ca biên của vị từ CHECK (đặc biệt NULL, lý do rỗng, mốc trạng thái, tách người) bằng SQL trong bộ nhớ. Kiểm tra cú pháp biểu thức lọc của 12 unique trong cùng bộ đánh giá. Đây không phải trình phân tích T-SQL và không thay thế kiểm tra SQL Server.
- Đánh giá thêm 13 ca dẫn xuất thanh toán: đủ tám trạng thái, hoàn đủ/một phần, bỏ phiếu bị từ chối, ưu tiên khi nhiều nguồn cùng có và đúng một dòng/đơn. Bộ đánh giá SQLite trong bộ nhớ đổi ISNULL thành IFNULL và bỏ tiền tố N của literal; chỉ kiểm logic view, không kiểm cú pháp T-SQL.
- Chưa chạy SQL Server, chưa xác nhận tiêu chí “chạy sạch database trống, chạy lại không lỗi”. Người giám sát chạy thử 001–004 lần đầu/lần hai và bộ cài trên SQL Server 2022.
