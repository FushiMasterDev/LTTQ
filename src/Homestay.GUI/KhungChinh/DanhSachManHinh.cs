using Homestay.BLL.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

/// <summary>Một màn hình trong menu: mã, tiêu đề, dành cho chức vụ nào và cách tạo form.</summary>
internal sealed record ScreenEntry(string Id, string Title, string Audience, Func<Form> Create)
{
    public override string ToString() => Id + "  " + Title;
}

// Layout inventory stays separate from the integrated allowlist; production always checks BLL authorization.
/// <summary>Danh sách tất cả màn hình của ứng dụng theo chức vụ, dùng để dựng menu và chế độ xem bố cục.</summary>
internal static class ScreenRegistry
{
    public static readonly IReadOnlySet<string> EnabledProductionForms = new HashSet<string> { "F14", "F15", "F16", "F19", "F22", "F23", "F02", "F05", "F06", "F07", "F08", "F09", "F10", "F11", "F13", "F18", "F20", "F24", "F27", "F28", "F29", "F30", "F31", "F32" };
    public static readonly ScreenEntry[] Layouts =
    [
        new("F01", "Đăng nhập", "All", () => new Shell.FrmLogin()),
        new("F02", "Đổi mật khẩu", "All", () => new Shell.FrmChangePassword()),
        new("F04", "Cấu hình kết nối", "All", () => new Shell.FrmConnection()),
        new("F05", "Tổng quan", "All", () => new Staff.FrmDashboard()),
        new("F06", "Tra phòng & lịch phòng", "Operations", () => new Staff.FrmAvailability()),
        new("F07", "Tạo đơn", "Operations", () => new Staff.FrmBookingCreate()),
        new("F08", "Danh sách đơn", "All", () => new Staff.FrmBookings()),
        new("F09", "Chi tiết đơn", "All", () => new Staff.FrmBookingDetail()),
        new("F10", "Ghi nhận thu tiền", "All", () => new Staff.FrmReceipt()),
        new("F11", "Yêu cầu hủy", "Operations", () => new Staff.FrmCancellation()),
        new("F13", "Khách hàng", "All", () => new Staff.FrmCustomers()),
        new("F14", "Của tôi", "All", () => new Staff.FrmMyWork()),
        new("F15", "Danh sách homestay", "Operations", () => new Manager.FrmHomestays()),
        new("F16", "Hồ sơ homestay", "Operations", () => new Manager.FrmHomestayProfile()),
        new("F18", "Nhân viên & tài khoản", "Manager", () => new Manager.FrmStaff()),
        new("F19", "Giao việc", "Manager", () => new Manager.FrmTasks()),
        new("F20", "Duyệt hủy", "Manager", () => new Manager.FrmCancellationApproval()),
        new("F22", "Chủ homestay", "Operations", () => new Manager.FrmOwners()),
        new("F23", "Hợp đồng", "Manager", () => new Manager.FrmContracts()),
        new("F24", "Danh mục & cấu hình", "Manager", () => new Manager.FrmCatalog()),
        new("F25", "Báo cáo", "Finance", () => new Manager.FrmReports()),
        new("F26", "Nhật ký hệ thống", "Manager", () => new Manager.FrmAudit()),
        new("F27", "Xác minh thu cọc", "Accountant", () => new Accountant.FrmDepositVerification()),
        new("F28", "Sao kê ngân hàng", "Accountant", () => new Accountant.FrmBankStatement()),
        new("F29", "Hàng chờ hoàn tiền", "Accountant", () => new Accountant.FrmRefundQueue()),
        new("F30", "Danh sách đối soát", "Finance", () => new Accountant.FrmSettlements()),
        new("F31", "Chi tiết đối soát", "Finance", () => new Accountant.FrmSettlementDetail()),
        new("F32", "Sổ thu chi & công nợ", "Finance", () => new Accountant.FrmCashBook()),
    ];
    public static ScreenEntry[] Production(IAccountGateway gateway, AppServices? services)
    {
        var entries = new List<ScreenEntry>();
        if (EnabledProductionForms.Contains("F02") && gateway.Can(Permission.AccountSelfManage))
            entries.Add(new("F02","Đổi mật khẩu","All",()=>new FrmChangePassword(gateway)));
        if (EnabledProductionForms.Contains("F18") && gateway.Can(Permission.EmployeeManage))
            entries.Add(new("F18","Nhân viên & tài khoản","Manager",()=>services is null
                ? throw new NotImplementedException() : new Manager.FrmStaff(services)));
        if (EnabledProductionForms.Contains("F24") && gateway.Can(Permission.AccountManage) && gateway.Can(Permission.CatalogManage))
            entries.Add(new("F24","Danh mục & cấu hình","Manager",()=>services is null
                ? throw new NotImplementedException() : new Manager.FrmCatalog(services)));
        if(services is not null) {
            if(gateway.Can(Permission.OwnerEdit))entries.Add(new("F22","Chủ homestay","Operations",()=>new Manager.FrmOwners(services)));
            if(gateway.Can(Permission.HomestayView)){
                entries.Add(new("F15","Danh sách homestay","Operations",()=>new Manager.FrmHomestays(services)));
                entries.Add(new("F16","Hồ sơ homestay","Operations",()=>throw new InvalidOperationException("Chọn homestay trong danh sách.")));
            }
            if(gateway.Can(Permission.ContractManage)&&gateway.Session?.Position==Homestay.DTO.Enums.Position.Manager)entries.Add(new("F23","Hợp đồng","Manager",()=>new Manager.FrmContracts(services)));
            if(gateway.Can(Permission.WorkTaskAssign))entries.Add(new("F19","Giao việc","Manager",()=>new Manager.FrmTasks(services)));
            if(gateway.Can(Permission.OwnWorkTaskManage))entries.Add(new("F14","Việc của tôi","All",()=>new Staff.FrmMyWork(services)));
            if(gateway.Can(Permission.BookingCancellationRequest))entries.Add(new("F11","Yêu cầu hủy","Operations",()=>throw new InvalidOperationException("Chọn đơn để xem tiền hoàn.")));
            if(gateway.Can(Permission.BookingChangeReview))entries.Add(new("F20","Duyệt yêu cầu hủy","Manager",()=>new Manager.FrmCancellationApproval(services)));
            if(gateway.Can(Permission.PaymentVerify))entries.Add(new("F27","Hàng chờ xác minh cọc","Accountant",()=>new Accountant.FrmDepositVerification(services)));
            if(gateway.Can(Permission.BankStatementManage))entries.Add(new("F28","Sao kê ngân hàng","Accountant",()=>new Accountant.FrmBankStatement(services)));
            if(gateway.Can(Permission.BookingRefundRecord))entries.Add(new("F29","Hàng chờ hoàn tiền","Accountant",()=>new Accountant.FrmRefundQueue(services)));
            if(gateway.Can(Permission.SettlementView))entries.Add(new("F30","Danh sách đối soát","Finance",()=>new Accountant.FrmSettlements(services)));
            if(gateway.Can(Permission.CashBookManage))entries.Add(new("F32","Sổ thu chi & công nợ","Finance",()=>new Accountant.FrmCashBook(services)));
            if(gateway.Can(Permission.BookingView)) {
                entries.Add(new("F05","Tổng quan","All",()=>new Staff.FrmDashboard(services)));
                entries.Add(new("F08","Danh sách đơn","All",()=>new Staff.FrmBookings(services)));
                entries.Add(new("F09","Chi tiết đơn","All",()=>throw new InvalidOperationException("Chọn một đơn trong danh sách.")));
            }
            if(gateway.Can(Permission.AvailabilitySearch)) entries.Add(new("F06","Tra phòng & lịch","Operations",()=>new Staff.FrmAvailability(services)));
            if(gateway.Can(Permission.BookingCreate))entries.Add(new("F07","Tạo đơn","Operations",()=>new Staff.FrmBookingCreate(services)));
            if(gateway.Can(Permission.PaymentRecord))entries.Add(new("F10","Thu cọc","Operations",()=>throw new InvalidOperationException("Chọn một đơn để ghi nhận cọc.")));
            if(gateway.Can(Permission.CustomerManage))entries.Add(new("F13","Khách hàng","All",()=>new Staff.FrmCustomers(services)));
        }
        return entries.Where(e=>EnabledProductionForms.Contains(e.Id)).OrderBy(e=>e.Id).ToArray();
    }
    public static bool IsPreviewAudience(ScreenEntry screen, string role) => screen.Audience switch
    {
        "All" => true,
        "Manager" => role == "Quản lý",
        "Accountant" => role == "Kế toán",
        "Operations" => role is "Nhân viên" or "Quản lý",
        "Finance" => role is "Kế toán" or "Quản lý",
        _ => false
    };
}
