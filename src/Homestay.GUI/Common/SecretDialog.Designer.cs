#nullable enable
namespace Homestay.GUI.Common;
partial class SecretDialog
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel root = null!;
    private Label accountLabel = null!;
    private Label notice = null!;
    private TextBox secretBox = null!;
    private Button closeButton = null!;
    protected override void Dispose(bool disposing)
    {
        if(disposing) components?.Dispose();
        base.Dispose(disposing);
    }
    private void InitializeComponent()
    {
        components=new System.ComponentModel.Container();
        SuspendLayout();
        root=new TableLayoutPanel();
        accountLabel=new Label();
        notice=new Label();
        secretBox=new TextBox();
        closeButton=new Button();
        Text="Mật khẩu tạm";
        root.Dock=DockStyle.Fill;
        root.Padding=new Padding(24);
        root.RowCount=4;
        root.ColumnCount=1;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        accountLabel.AutoSize=true;
        notice.Text="Mật khẩu tạm chỉ hiển thị ở lần này. Hãy chuyển cho đúng nhân viên; nhân viên phải đổi ở lần đăng nhập đầu.";
        notice.Dock=DockStyle.Fill;
        notice.Padding=new Padding(0,20,0,10);
        secretBox.ReadOnly=true;
        secretBox.Dock=DockStyle.Top;
        secretBox.Font=new Font("Consolas",16);
        closeButton.Text="Đã ghi lại";
        closeButton.AutoSize=true;
        root.Controls.Add(accountLabel,0,0);
        root.Controls.Add(notice,0,1);
        root.Controls.Add(secretBox,0,2);
        root.Controls.Add(closeButton,0,3);
        Controls.Add(root);
        AutoScaleDimensions=new SizeF(96,96);
        AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(640,400);
        MinimumSize=new Size(520,400);
        Font=new Font("Segoe UI",10);
        StartPosition=FormStartPosition.CenterParent;
        ResumeLayout(false);
        PerformLayout();
    }
}
