using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmAudit : ScreenForm
{
    public FrmAudit()
    {
        InitializeComponent();
        HelpText = "Nhật ký chỉ đọc và không hiển thị mật khẩu/CCCD đầy đủ. Chưa nối ReportService/AuditLogs.";
    }
}
