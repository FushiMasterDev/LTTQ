using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

public partial class FrmDashboard : ScreenForm
{
    public FrmDashboard()
    {
        InitializeComponent();
        HelpText = "Tổng quan sẽ hiển thị theo vai trò từ BLL. Các chỉ tiêu là liên kết tới danh sách đã lọc; chưa hiển thị số khi chưa có dữ liệu.";
    }
}
