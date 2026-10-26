using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmReceipt : ScreenForm
{
    public FrmReceipt()
    {
        InitializeComponent();
        HelpText = "Phiếu thu mới có trạng thái Chờ xác minh. Người ghi không tự xác minh. Chứng từ tối đa 5 MB; kiểm tệp và quyền do FileService/BLL thực hiện.";
    }
}
