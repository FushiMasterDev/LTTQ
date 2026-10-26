using Homestay.GUI.Common;

namespace Homestay.GUI.Accountant;

public partial class FrmRefundQueue : ScreenForm
{
    public FrmRefundQueue()
    {
        InitializeComponent();
        HelpText = "Chỉ có tab Hoàn tiền. Khoản bù và Cọc bổ sung tạm ẩn. Ghi đã chi cần mã giao dịch, ngày, chứng từ và kiểm đồng thời tại BLL.";
    }
}
