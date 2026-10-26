#nullable enable
namespace Homestay.GUI.Shell;

partial class FrmLogin
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel root = null!;
    private Label titleLabel = null!;
    private Label noticeLabel = null!;
    private FlowLayoutPanel actions = null!;
    private Label usernameBoxLabel = null!;
    private TextBox usernameBox = null!;
    private Label passwordBoxLabel = null!;
    private TextBox passwordBox = null!;
    private Label showPasswordLabel = null!;
    private CheckBox showPassword = null!;
    private Button cancelButton = null!;
    private Button loginButton = null!;
    private Button helpButton = null!;
    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();
        root = new TableLayoutPanel();
        titleLabel = new Label();
        noticeLabel = new Label();
        actions = new FlowLayoutPanel();
        usernameBoxLabel = new Label();
        usernameBox = new TextBox();
        passwordBoxLabel = new Label();
        passwordBox = new TextBox();
        showPasswordLabel = new Label();
        showPassword = new CheckBox();
        cancelButton = new Button();
        loginButton = new Button();
        helpButton = new Button();
        root.Dock = DockStyle.Fill;
        root.Padding = new Padding(28);
        root.ColumnCount = 2;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        root.RowCount = 6;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLabel.Text = "F01 · Đăng nhập";

        titleLabel.Font = new Font("Segoe UI", 20F, FontStyle.Bold);
        titleLabel.ForeColor = Common.UiTheme.Accent;
        titleLabel.AutoSize = true;
        titleLabel.Margin = new Padding(0,0,0,20);
        root.Controls.Add(titleLabel,0,0);
        root.SetColumnSpan(titleLabel,2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        usernameBoxLabel.Text = "Mã nhân viên";
        usernameBox.AccessibleName = "Mã nhân viên";
        usernameBoxLabel.AutoSize = true;
        usernameBoxLabel.Margin = new Padding(0,10,10,10);
        usernameBox.Dock = DockStyle.Fill;
        usernameBox.Margin = new Padding(0,6,0,6);
        usernameBox.TabIndex = 0;
        usernameBox.PlaceholderText = "Ví dụ: NV0012";
        usernameBox.MaxLength = 50;
        root.Controls.Add(usernameBoxLabel,0,1);
        root.Controls.Add(usernameBox,1,1);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        passwordBoxLabel.Text = "Mật khẩu";
        passwordBox.AccessibleName = "Mật khẩu";
        passwordBoxLabel.AutoSize = true;
        passwordBoxLabel.Margin = new Padding(0,10,10,10);
        passwordBox.Dock = DockStyle.Fill;
        passwordBox.Margin = new Padding(0,6,0,6);
        passwordBox.TabIndex = 1;
        passwordBox.UseSystemPasswordChar = true;
        root.Controls.Add(passwordBoxLabel,0,2);
        root.Controls.Add(passwordBox,1,2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        showPasswordLabel.Text = "";
        showPassword.AccessibleName = "";
        showPasswordLabel.AutoSize = true;
        showPasswordLabel.Margin = new Padding(0,10,10,10);
        showPassword.Dock = DockStyle.Fill;
        showPassword.Margin = new Padding(0,6,0,6);
        showPassword.TabIndex = 2;
        showPassword.Text = "Hiện mật khẩu";
        root.Controls.Add(showPasswordLabel,0,3);
        root.Controls.Add(showPassword,1,3);
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        noticeLabel.Text = "Fuji Homestay · Phiên bản 0.1\nChưa xác thực kết nối. Đăng nhập chỉ được thực hiện qua BLL.";

        noticeLabel.Dock = DockStyle.Fill;
        noticeLabel.ForeColor = Common.UiTheme.Ink;
        noticeLabel.BackColor = Common.UiTheme.Warning;
        noticeLabel.Padding = new Padding(12);
        noticeLabel.MinimumSize = new Size(0,90);
        root.Controls.Add(noticeLabel,0,4);
        root.SetColumnSpan(noticeLabel,2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        actions.AutoSize = true;
        actions.Dock = DockStyle.Fill;
        actions.FlowDirection = FlowDirection.LeftToRight;
        actions.WrapContents = true;
        cancelButton.Name = "cancelButton";
        cancelButton.Text = "Thoát";
        cancelButton.AutoSize = true;
        cancelButton.MinimumSize = new Size(100,36);
        cancelButton.Tag = "close";
        actions.Controls.Add(cancelButton);
        loginButton.Name = "loginButton";
        loginButton.Text = "Đăng nhập";
        loginButton.AutoSize = true;
        loginButton.MinimumSize = new Size(100,36);
        actions.Controls.Add(loginButton);
        helpButton.Name = "helpButton";
        helpButton.Text = "?";
        helpButton.AutoSize = true;
        helpButton.MinimumSize = new Size(100,36);
        helpButton.Tag = "help";
        actions.Controls.Add(helpButton);
        root.Controls.Add(actions,0,5);
        root.SetColumnSpan(actions,2);
        Controls.Add(root);
        Text = "F01 · Đăng nhập";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 620);
        MinimumSize = new Size(620, 480);
        Font = new Font("Segoe UI", 10F);
        StartPosition = FormStartPosition.CenterScreen;
        ResumeLayout(false);
        PerformLayout();
    }
}
