using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

public partial class FrmTasks : ScreenForm
{
    public FrmTasks()
    {
        InitializeComponent();
        HelpText = "Giao việc có hạn và ưu tiên. Việc quá hạn hiển thị đỏ khi có dữ liệu thật. Chưa nối TaskService.";
    }
}
