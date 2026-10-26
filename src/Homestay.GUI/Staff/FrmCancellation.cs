using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmCancellation : ScreenForm
{
    public FrmCancellation()
    {
        InitializeComponent();
        HelpText = "Giai đoạn này chỉ hỗ trợ Hủy. Muốn đổi ngày/loại phòng: hủy theo chính sách rồi đặt đơn mới. Tiền hoàn do hệ thống tính.";
    }
}
