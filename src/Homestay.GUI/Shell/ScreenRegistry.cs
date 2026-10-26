using Homestay.BLL.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

internal sealed record ScreenEntry(string Id, string Title, string Audience, Func<Form> Create)
{
    public override string ToString() => Id + "  " + Title;
}

// Layout inventory stays separate from the integrated allowlist; production always checks BLL authorization.
internal static class ScreenRegistry
{
    public static readonly IReadOnlySet<string> EnabledProductionForms = new HashSet<string> { "F02", "F18", "F24" };
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
        return entries.ToArray();
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
