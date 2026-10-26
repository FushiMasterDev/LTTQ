using Homestay.GUI.Common;

namespace Homestay.GUI.Accountant;

public partial class FrmBankStatement : ScreenForm
{
    public FrmBankStatement()
    {
        InitializeComponent();
        HelpText = "Giai đoạn này chỉ nhập tay; nhập tệp chờ mẫu MB Bank. BLL chặn giao dịch trùng và kiểm tiền/thời gian. Chưa nối dịch vụ sao kê.";
    }
}
