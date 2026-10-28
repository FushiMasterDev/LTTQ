using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

/// <summary>Màn hình báo cáo theo quyền (kế toán chỉ thấy báo cáo tài chính).</summary>
public partial class FrmReports : ScreenForm
{
    public FrmReports()
    {
        InitializeComponent();
        HelpText = "Tab báo cáo theo quyền từ hệ thống; Kế toán chỉ thấy tài chính. Mọi công thức dùng hệ thống, không tự tính trong GUI.";
    }
}
