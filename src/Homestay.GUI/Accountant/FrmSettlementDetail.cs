using Homestay.GUI.Common;

namespace Homestay.GUI.Accountant;

public partial class FrmSettlementDetail : ScreenForm
{
    public FrmSettlementDetail()
    {
        InitializeComponent();
        HelpText = "Không gửi khi còn đơn chưa chốt. Chủ không xác nhận trong ứng dụng: Nháp → Đã gửi → Đã trả. PrintDocument cần dữ liệu và tiền bằng chữ do BLL cung cấp.";
    }
}
