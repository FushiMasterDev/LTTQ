namespace Homestay.BLL.Infrastructure;

public enum Permission
{
    /// <summary>Đăng nhập, đổi mật khẩu của mình</summary>
    AccountSelfManage,
    /// <summary>Tạo nhân viên, đổi chức vụ, nghỉ việc</summary>
    EmployeeManage,
    /// <summary>Khóa/mở khóa, đặt lại mật khẩu tài khoản khác</summary>
    AccountManage,
    /// <summary>Gán khu vực cho nhân viên</summary>
    EmployeeAreaAssign,
    /// <summary>Xem phiên đăng nhập, buộc đăng xuất</summary>
    SessionManage,
    /// <summary>Danh mục: khu vực, tiện nghi, danh mục phòng, ngày lễ</summary>
    CatalogManage,
    /// <summary>Chính sách hủy chung</summary>
    CancellationPolicyManage,
    /// <summary>Cấu hình tham số nghiệp vụ</summary>
    SettingsManage,
    /// <summary>Xem nhật ký hệ thống (AuditLogs)</summary>
    AuditView,
    /// <summary>Tạo/sửa hồ sơ chủ</summary>
    OwnerEdit,
    /// <summary>Duyệt chủ</summary>
    OwnerApprove,
    /// <summary>Đổi tài khoản ngân hàng của chủ</summary>
    OwnerBankAccountChange,
    /// <summary>Tạo homestay nháp</summary>
    HomestayCreate,
    /// <summary>Sửa thông tin, tiện nghi, ảnh, giấy tờ</summary>
    HomestayEdit,
    /// <summary>Gửi homestay đi duyệt</summary>
    HomestaySubmit,
    /// <summary>Duyệt / trả về / tạm ngưng / mở lại / kết thúc homestay</summary>
    HomestayReview,
    /// <summary>Tạo, gia hạn, chấm dứt hợp đồng</summary>
    ContractManage,
    /// <summary>Xem giấy tờ pháp lý</summary>
    LegalDocumentView,
    /// <summary>Thêm/sửa/bật/tắt loại phòng, phòng</summary>
    RoomManage,
    /// <summary>Thêm phiên bản giá</summary>
    RoomPriceAdd,
    /// <summary>Chính sách hủy riêng</summary>
    HomestayCancellationPolicyManage,
    /// <summary>Xem lịch phòng</summary>
    RoomCalendarView,
    /// <summary>Khóa / mở khóa phòng</summary>
    RoomBlockManage,
    /// <summary>Tra phòng trống, báo giá</summary>
    AvailabilitySearch,
    /// <summary>Tìm, tạo khách; sửa tên/email/ghi chú</summary>
    CustomerManage,
    /// <summary>Xem CCCD đầy đủ</summary>
    CustomerIdentityView,
    /// <summary>Sửa SĐT khách đã có đơn</summary>
    CustomerPhoneChange,
    /// <summary>Tạo đơn, áp voucher của khách</summary>
    BookingCreate,
    /// <summary>Xem danh sách, chi tiết đơn</summary>
    BookingView,
    /// <summary>Nhận phụ trách đơn chưa giao</summary>
    BookingClaim,
    /// <summary>Giao/giao lại đơn cho người khác</summary>
    BookingAssign,
    /// <summary>Ghi nhận thu cọc / cọc bổ sung (khách báo đã chuyển)</summary>
    PaymentRecord,
    /// <summary>Xác nhận / từ chối đơn</summary>
    BookingConfirm,
    /// <summary>Đổi phòng cùng loại khi xác nhận</summary>
    BookingRoomChange,
    /// <summary>Nhận phòng / trả phòng / không đến</summary>
    BookingStayManage,
    /// <summary>Hủy đơn chưa có tiền (Chờ thanh toán)</summary>
    BookingCancelUnpaid,
    /// <summary>Gửi yêu cầu hủy đơn đã cọc</summary>
    BookingCancellationRequest,
    /// <summary>Gửi yêu cầu đổi ngày / loại phòng</summary>
    BookingChangeRequest,
    /// <summary>Duyệt / từ chối yêu cầu đổi/hủy</summary>
    BookingChangeReview,
    /// <summary>Báo sự cố trùng phòng</summary>
    BookingOverlapReport,
    /// <summary>Trùng phòng: chuyển phòng cùng loại, tạo đơn thay thế</summary>
    BookingOverlapResolve,
    /// <summary>Trùng phòng: hủy + hoàn + voucher</summary>
    BookingOverlapCompensate,
    /// <summary>Cấp / thu hồi voucher</summary>
    VoucherManage,
    /// <summary>Xác minh / từ chối phiếu thu</summary>
    PaymentVerify,
    /// <summary>Nhập sao kê, tự khớp, khớp tay, bỏ qua giao dịch</summary>
    BankStatementManage,
    /// <summary>Ghi nhận đã hoàn tiền giao dịch không khớp</summary>
    UnmatchedPaymentRefund,
    /// <summary>Ghi nhận đã chi hoàn tiền cho đơn</summary>
    BookingRefundRecord,
    /// <summary>Ghi nhận đã chi / từ chối khoản bù</summary>
    CompensationManage,
    /// <summary>Xác nhận cọc bổ sung của yêu cầu đổi</summary>
    ChangeDepositConfirm,
    /// <summary>Lập bảng đối soát nháp</summary>
    SettlementCreate,
    /// <summary>Gửi / mở lại / ghi nhận đã trả đối soát, ghi phản hồi của chủ</summary>
    SettlementManage,
    /// <summary>Xem đối soát</summary>
    SettlementView,
    /// <summary>Sổ thu chi, công nợ, in phiếu</summary>
    CashBookManage,
    /// <summary>Hiệu suất của chính mình</summary>
    OwnPerformanceView,
    /// <summary>Báo cáo vận hành, hiệu suất mọi nhân viên, công suất phòng</summary>
    OperationsReportView,
    /// <summary>Báo cáo tài chính công ty</summary>
    FinancialReportView,
    /// <summary>Doanh thu theo homestay</summary>
    HomestayRevenueView,
    /// <summary>Giao việc (WorkTasks)</summary>
    WorkTaskAssign,
    /// <summary>Xem / cập nhật việc được giao cho mình</summary>
    OwnWorkTaskManage,
    /// <summary>Đọc hồ sơ homestay trong khu vực được giao</summary>
    HomestayView,
    /// <summary>Xem định danh đầy đủ của chủ</summary>
    OwnerIdentityView,
    /// <summary>Nhập tài khoản nhận hoàn cho phiếu Refund Pending</summary>
    RefundAccountEdit,
}

public enum AccessLevel
{
    None, All, InScope, DraftOnly, OnBehalfOfOwner, OnBehalfOfOwnerInScope, ReadOnly, Own, FinancialTables
}
