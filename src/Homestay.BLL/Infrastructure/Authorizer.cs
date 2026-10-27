using System.Collections.Frozen;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Infrastructure;

public sealed class Authorizer(ICurrentSession currentSession)
{
    // Dịch vụ phải kiểm tra thêm: không tự khóa/hạ chức/nghỉ việc; còn >= 1 QL;
    // xác minh ngân hàng; không tự duyệt nháp/yêu cầu; tách người ghi/xác minh tiền;
    // TC phải có ghi chú. Own của BookingAssign là người đang phụ trách.
    private static readonly FrozenDictionary<Permission, (AccessLevel Staff, AccessLevel Manager, AccessLevel Accountant)> Matrix =
        new Dictionary<Permission, (AccessLevel, AccessLevel, AccessLevel)>
        {
            [Permission.HomestayView] = (AccessLevel.InScope, AccessLevel.InScope, AccessLevel.None),
            [Permission.OwnerIdentityView] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.RefundAccountEdit] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            [Permission.AccountSelfManage] = (AccessLevel.All, AccessLevel.All, AccessLevel.All),
            // Service cấm tự hạ chức/tự cho nghỉ việc và phải giữ ít nhất một Quản lý hoạt động.
            [Permission.EmployeeManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            // Service cấm tự khóa mình và phải giữ ít nhất một Quản lý hoạt động.
            [Permission.AccountManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.EmployeeAreaAssign] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.SessionManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.CatalogManage] = (AccessLevel.ReadOnly, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.CancellationPolicyManage] = (AccessLevel.ReadOnly, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.SettingsManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.AuditView] = (AccessLevel.Own, AccessLevel.All, AccessLevel.FinancialTables),
            // DraftOnly ở hồ sơ chủ nghĩa là Owner.Status = Pending.
            [Permission.OwnerEdit] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.OwnerApprove] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            // Quản lý chỉ đổi ngân hàng sau khi xác minh.
            [Permission.OwnerBankAccountChange] = (AccessLevel.None, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.HomestayCreate] = (AccessLevel.All, AccessLevel.All, AccessLevel.None),
            [Permission.HomestayEdit] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            [Permission.HomestaySubmit] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            // Service cấm duyệt homestay do chính người duyệt tạo nháp.
            [Permission.HomestayReview] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.ContractManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.LegalDocumentView] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            [Permission.RoomManage] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            [Permission.RoomPriceAdd] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            [Permission.HomestayCancellationPolicyManage] = (AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None),
            [Permission.RoomCalendarView] = (AccessLevel.All, AccessLevel.All, AccessLevel.None),
            [Permission.RoomBlockManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.AvailabilitySearch] = (AccessLevel.All, AccessLevel.All, AccessLevel.None),
            [Permission.CustomerManage] = (AccessLevel.All, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.CustomerIdentityView] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.CustomerPhoneChange] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.BookingCreate] = (AccessLevel.All, AccessLevel.All, AccessLevel.None),
            [Permission.BookingView] = (AccessLevel.All, AccessLevel.All, AccessLevel.ReadOnly),
            [Permission.BookingClaim] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            // Own ở đây là đơn mình đang phụ trách, không phải đơn mình tạo.
            [Permission.BookingAssign] = (AccessLevel.Own, AccessLevel.All, AccessLevel.None),
            [Permission.PaymentRecord] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.All),
            [Permission.BookingConfirm] = (AccessLevel.OnBehalfOfOwnerInScope, AccessLevel.OnBehalfOfOwner, AccessLevel.None),
            [Permission.BookingRoomChange] = (AccessLevel.OnBehalfOfOwnerInScope, AccessLevel.All, AccessLevel.None),
            [Permission.BookingStayManage] = (AccessLevel.OnBehalfOfOwnerInScope, AccessLevel.OnBehalfOfOwner, AccessLevel.None),
            [Permission.BookingCancelUnpaid] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            [Permission.BookingCancellationRequest] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            [Permission.BookingChangeRequest] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            // Service cấm duyệt yêu cầu do chính người duyệt gửi.
            [Permission.BookingChangeReview] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.BookingOverlapReport] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            [Permission.BookingOverlapResolve] = (AccessLevel.InScope, AccessLevel.All, AccessLevel.None),
            [Permission.BookingOverlapCompensate] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.VoucherManage] = (AccessLevel.None, AccessLevel.All, AccessLevel.ReadOnly),
            // Service cấm xác minh phiếu do chính người xác minh ghi nhận; ngoại lệ tự khớp sao kê theo spec 5.3.
            [Permission.PaymentVerify] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            [Permission.BankStatementManage] = (AccessLevel.None, AccessLevel.ReadOnly, AccessLevel.All),
            [Permission.UnmatchedPaymentRefund] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            [Permission.BookingRefundRecord] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            [Permission.CompensationManage] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            // Người xác nhận tiền phải khác người gửi và người duyệt yêu cầu.
            [Permission.ChangeDepositConfirm] = (AccessLevel.None, AccessLevel.None, AccessLevel.All),
            [Permission.SettlementCreate] = (AccessLevel.None, AccessLevel.All, AccessLevel.All),
            [Permission.SettlementManage] = (AccessLevel.None, AccessLevel.ReadOnly, AccessLevel.All),
            [Permission.SettlementView] = (AccessLevel.None, AccessLevel.All, AccessLevel.All),
            [Permission.CashBookManage] = (AccessLevel.None, AccessLevel.ReadOnly, AccessLevel.All),
            [Permission.OwnPerformanceView] = (AccessLevel.Own, AccessLevel.Own, AccessLevel.None),
            [Permission.OperationsReportView] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.FinancialReportView] = (AccessLevel.None, AccessLevel.All, AccessLevel.All),
            [Permission.HomestayRevenueView] = (AccessLevel.None, AccessLevel.All, AccessLevel.All),
            [Permission.WorkTaskAssign] = (AccessLevel.None, AccessLevel.All, AccessLevel.None),
            [Permission.OwnWorkTaskManage] = (AccessLevel.Own, AccessLevel.Own, AccessLevel.Own),
        }.ToFrozenDictionary();

    public AccessLevel GetAccess(Permission permission) => GetAccess(currentSession.RequireSession().Position, permission);
    public static AccessLevel GetAccess(Position position, Permission permission)
    {
        if (!Matrix.TryGetValue(permission, out var access)) return AccessLevel.None;
        return position switch
        {
            Position.Staff => access.Staff,
            Position.Manager => access.Manager,
            Position.Accountant => access.Accountant,
            _ => AccessLevel.None
        };
    }
    public bool Can(Permission permission) => GetAccess(permission) != AccessLevel.None;
    public void Require(Permission permission)
    {
        if (!Can(permission)) throw new ForbiddenException();
    }
}
