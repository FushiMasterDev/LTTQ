using Homestay.GUI.Common;

namespace Homestay.GUI.Staff;

/// <summary>Màn hình việc của tôi: các công việc được giao cho người đang đăng nhập.</summary>
public partial class FrmMyWork : ScreenForm
{
    public FrmMyWork()
    {
        InitializeComponent();
        HelpText = "Chỉ lấy dữ liệu của phiên đăng nhập hiện tại. Kết quả và hoàn thành công việc phải lưu qua hệ thống. Chức năng đang được hoàn thiện.";
    }
    public FrmMyWork(AppServices services):this(){root.Visible=false;var board=new WorkTaskBoard(services,false){TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill};Controls.Add(board);board.BringToFront();Shown+=(_,_)=>board.Show();Disposed+=(_,_)=>board.Dispose();}
}
