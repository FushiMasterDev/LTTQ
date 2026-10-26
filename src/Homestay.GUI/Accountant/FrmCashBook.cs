using Homestay.GUI.Common;

namespace Homestay.GUI.Accountant;

public partial class FrmCashBook : ScreenForm
{
    public FrmCashBook()
    {
        InitializeComponent();
        HelpText = "Chỉ dòng tiền đã xác minh vào sổ. Mở chứng từ gốc để xử lý công nợ. Bản in cần dữ liệu thật và số tiền bằng chữ từ hệ thống; chưa nối dịch vụ tài chính.";
    }
}
