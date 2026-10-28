using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

/// <summary>Màn hình xem nhật ký hệ thống (ai đã làm gì, khi nào).</summary>
public partial class FrmAudit : ScreenForm
{
    public FrmAudit()
    {
        InitializeComponent();
        HelpText = "Nhật ký chỉ đọc và không hiển thị mật khẩu/CCCD đầy đủ. Chức năng đang được hoàn thiện.";
    }
}
