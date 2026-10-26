using Homestay.GUI.Common;

namespace Homestay.GUI.Accountant;

public partial class FrmDepositVerification : ScreenForm
{
    public FrmDepositVerification()
    {
        InitializeComponent();
        HelpText = "Người ghi nhận không tự xác minh. Đối chiếu chứng từ và sao kê trước khi xác minh. Từ chối cần lý do. Chưa nối PaymentService.";
    }
}
