using Homestay.GUI.Common;

namespace Homestay.GUI.Manager;

/// <summary>Màn hình giao việc của quản lý (dùng bảng công việc chung).</summary>
public partial class FrmTasks : ScreenForm
{
    public FrmTasks()
    {
        InitializeComponent();
        HelpText = "Giao việc có hạn và ưu tiên. Việc quá hạn hiển thị đỏ khi có dữ liệu thật. Dữ liệu và thao tác được kiểm tra qua dịch vụ.";
    }
    public FrmTasks(AppServices services,TaskBoardSelection? selection=null):this(){root.Visible=false;var board=new WorkTaskBoard(services,true,selection){TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill};Controls.Add(board);board.BringToFront();Shown+=(_,_)=>board.Show();Disposed+=(_,_)=>board.Dispose();}
}
