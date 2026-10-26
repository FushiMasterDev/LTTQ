using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmCancellationApproval : ScreenForm
{
    public FrmCancellationApproval()
    {
        InitializeComponent();
        HelpText = "Chỉ duyệt loại Hủy trong kế hoạch này. Người gửi không tự duyệt. BLL xem trước tiền hoàn, kiểm quyền và ghi lịch sử trong cùng giao dịch.";
    }
}
