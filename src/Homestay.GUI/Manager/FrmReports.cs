using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmReports : ScreenForm
{
    public FrmReports()
    {
        InitializeComponent();
        HelpText = "Tab báo cáo theo quyền từ BLL; Kế toán chỉ thấy tài chính. Mọi công thức dùng ReportService, không tự tính trong GUI.";
    }
}
