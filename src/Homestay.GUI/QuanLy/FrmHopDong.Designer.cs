#nullable enable
namespace Homestay.GUI.Manager;

partial class FrmContracts
{
    private System.ComponentModel.IContainer? components = new System.ComponentModel.Container();
    private TableLayoutPanel root = null!;
    private Label heading = null!;
    private Label pendingNotice = null!;
    private TableLayoutPanel body = null!;
    private TableLayoutPanel content = null!;
    private FlowLayoutPanel toolbar = null!;
    private Button helpButton = null!;
    private Button reloadButton = null!;
    private Button addButton = null!;
    private Button renewButton = null!;
    private Button endButton = null!;
    private Panel filterViewport = null!;
    private TableLayoutPanel filterPanel = null!;
    private Label filterPanelField0Label = null!;
    private TextBox filterPanelField0 = null!;
    private Label filterPanelField1Label = null!;
    private Common.SearchPicker filterPanelField1 = null!;
    private Label filterPanelField2Label = null!;
    private ComboBox filterPanelField2 = null!;
    private Label filterPanelField3Label = null!;
    private DateTimePicker filterPanelField3 = null!;
    private Label filterPanelField4Label = null!;
    private DateTimePicker filterPanelField4 = null!;
    private TabControl viewTabs = null!;
    private TabPage tab0 = null!;
    private Common.PagedGrid grid0 = null!;
    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }
    private void InitializeComponent()
    {
        root = new TableLayoutPanel();
        heading = new Label();
        pendingNotice = new Label();
        body = new TableLayoutPanel();
        filterViewport = new Panel();
        filterPanel = new TableLayoutPanel();
        filterPanelField0Label = new Label();
        filterPanelField0 = new TextBox();
        filterPanelField1Label = new Label();
        filterPanelField1 = new Homestay.GUI.Common.SearchPicker();
        filterPanelField2Label = new Label();
        filterPanelField2 = new ComboBox();
        filterPanelField3Label = new Label();
        filterPanelField3 = new DateTimePicker();
        filterPanelField4Label = new Label();
        filterPanelField4 = new DateTimePicker();
        content = new TableLayoutPanel();
        toolbar = new FlowLayoutPanel();
        reloadButton = new Button();
        addButton = new Button();
        renewButton = new Button();
        endButton = new Button();
        helpButton = new Button();
        viewTabs = new TabControl();
        tab0 = new TabPage();
        grid0 = new Homestay.GUI.Common.PagedGrid();
        root.SuspendLayout();
        body.SuspendLayout();
        filterViewport.SuspendLayout();
        filterPanel.SuspendLayout();
        content.SuspendLayout();
        toolbar.SuspendLayout();
        viewTabs.SuspendLayout();
        tab0.SuspendLayout();
        SuspendLayout();
        //
        // root
        //
        root.ColumnCount = 1;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(pendingNotice, 0, 1);
        root.Controls.Add(body, 0, 2);
        root.Dock = DockStyle.Fill;
        root.Location = new Point(0, 0);
        root.Name = "root";
        root.Padding = new Padding(16);
        root.RowCount = 3;
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.Size = new Size(1120, 670);
        root.TabIndex = 0;
        //
        // heading
        //
        heading.AutoSize = true;
        heading.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
        heading.ForeColor = Color.FromArgb(0, 103, 92);
        heading.Location = new Point(16, 16);
        heading.Margin = new Padding(0, 0, 0, 12);
        heading.Name = "heading";
        heading.Size = new Size(283, 48);
        heading.TabIndex = 0;
        heading.Text = "F23 · Hợp đồng";
        //
        // pendingNotice
        //
        pendingNotice.AutoSize = true;
        pendingNotice.BackColor = Color.FromArgb(255, 246, 220);
        pendingNotice.Dock = DockStyle.Fill;
        pendingNotice.Location = new Point(16, 76);
        pendingNotice.Margin = new Padding(0, 0, 0, 12);
        pendingNotice.Name = "pendingNotice";
        pendingNotice.Padding = new Padding(10);
        pendingNotice.Size = new Size(1088, 48);
        pendingNotice.TabIndex = 1;
        pendingNotice.Text = "BỐ CỤC GIAO DIỆN · Chưa nối dữ liệu / chưa ghi dữ liệu";
        //
        // body
        //
        body.ColumnCount = 2;
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.Controls.Add(filterViewport, 0, 0);
        body.Controls.Add(content, 1, 0);
        body.Dock = DockStyle.Fill;
        body.Location = new Point(19, 139);
        body.Name = "body";
        body.RowCount = 1;
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        body.Size = new Size(1082, 512);
        body.TabIndex = 2;
        //
        // filterViewport
        //
        filterViewport.AutoScroll = true;
        filterViewport.Controls.Add(filterPanel);
        filterViewport.Dock = DockStyle.Fill;
        filterViewport.Location = new Point(3, 3);
        filterViewport.Name = "filterViewport";
        filterViewport.Size = new Size(244, 506);
        filterViewport.TabIndex = 0;
        //
        // filterPanel
        //
        filterPanel.AutoSize = true;
        filterPanel.ColumnCount = 1;
        filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        filterPanel.Controls.Add(filterPanelField0Label, 0, 0);
        filterPanel.Controls.Add(filterPanelField0, 0, 1);
        filterPanel.Controls.Add(filterPanelField1Label, 0, 2);
        filterPanel.Controls.Add(filterPanelField1, 0, 3);
        filterPanel.Controls.Add(filterPanelField2Label, 0, 4);
        filterPanel.Controls.Add(filterPanelField2, 0, 5);
        filterPanel.Controls.Add(filterPanelField3Label, 0, 6);
        filterPanel.Controls.Add(filterPanelField3, 0, 7);
        filterPanel.Controls.Add(filterPanelField4Label, 0, 8);
        filterPanel.Controls.Add(filterPanelField4, 0, 9);
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Location = new Point(0, 0);
        filterPanel.Name = "filterPanel";
        filterPanel.Padding = new Padding(8);
        filterPanel.RowCount = 11;
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        filterPanel.Size = new Size(244, 457);
        filterPanel.TabIndex = 0;
        //
        // filterPanelField0Label
        //
        filterPanelField0Label.AutoSize = true;
        filterPanelField0Label.Location = new Point(11, 16);
        filterPanelField0Label.Margin = new Padding(3, 8, 3, 3);
        filterPanelField0Label.Name = "filterPanelField0Label";
        filterPanelField0Label.Size = new Size(175, 28);
        filterPanelField0Label.TabIndex = 0;
        filterPanelField0Label.Text = "Tìm tên / mã / SĐT";
        //
        // filterPanelField0
        //
        filterPanelField0.Dock = DockStyle.Top;
        filterPanelField0.Location = new Point(11, 50);
        filterPanelField0.Margin = new Padding(3, 3, 8, 6);
        filterPanelField0.Name = "filterPanelField0";
        filterPanelField0.Size = new Size(217, 34);
        filterPanelField0.TabIndex = 1;
        //
        // filterPanelField1Label
        //
        filterPanelField1Label.AutoSize = true;
        filterPanelField1Label.Location = new Point(11, 98);
        filterPanelField1Label.Margin = new Padding(3, 8, 3, 3);
        filterPanelField1Label.Name = "filterPanelField1Label";
        filterPanelField1Label.Size = new Size(100, 28);
        filterPanelField1Label.TabIndex = 2;
        filterPanelField1Label.Text = "Homestay";
        //
        // filterPanelField1
        //
        filterPanelField1.Dock = DockStyle.Top;
        filterPanelField1.Location = new Point(11, 132);
        filterPanelField1.Margin = new Padding(3, 3, 8, 6);
        filterPanelField1.MinimumSize = new Size(100, 66);
        filterPanelField1.Name = "filterPanelField1";
        filterPanelField1.Size = new Size(217, 66);
        filterPanelField1.TabIndex = 3;
        //
        // filterPanelField2Label
        //
        filterPanelField2Label.AutoSize = true;
        filterPanelField2Label.Location = new Point(11, 212);
        filterPanelField2Label.Margin = new Padding(3, 8, 3, 3);
        filterPanelField2Label.Name = "filterPanelField2Label";
        filterPanelField2Label.Size = new Size(98, 28);
        filterPanelField2Label.TabIndex = 4;
        filterPanelField2Label.Text = "Trạng thái";
        //
        // filterPanelField2
        //
        filterPanelField2.Dock = DockStyle.Top;
        filterPanelField2.DropDownStyle = ComboBoxStyle.DropDownList;
        filterPanelField2.Items.AddRange(new object[] { "Tất cả" });
        filterPanelField2.Location = new Point(11, 246);
        filterPanelField2.Margin = new Padding(3, 3, 8, 6);
        filterPanelField2.Name = "filterPanelField2";
        filterPanelField2.Size = new Size(217, 36);
        filterPanelField2.TabIndex = 5;
        //
        // filterPanelField3Label
        //
        filterPanelField3Label.AutoSize = true;
        filterPanelField3Label.Location = new Point(11, 293);
        filterPanelField3Label.Margin = new Padding(3, 8, 3, 3);
        filterPanelField3Label.Name = "filterPanelField3Label";
        filterPanelField3Label.Size = new Size(82, 28);
        filterPanelField3Label.TabIndex = 6;
        filterPanelField3Label.Text = "Từ ngày";
        //
        // filterPanelField3
        //
        filterPanelField3.CustomFormat = "dd/MM/yyyy";
        filterPanelField3.Dock = DockStyle.Top;
        filterPanelField3.Format = DateTimePickerFormat.Custom;
        filterPanelField3.Location = new Point(11, 327);
        filterPanelField3.Margin = new Padding(3, 3, 8, 6);
        filterPanelField3.Name = "filterPanelField3";
        filterPanelField3.Size = new Size(217, 34);
        filterPanelField3.TabIndex = 7;
        //
        // filterPanelField4Label
        //
        filterPanelField4Label.AutoSize = true;
        filterPanelField4Label.Location = new Point(11, 375);
        filterPanelField4Label.Margin = new Padding(3, 8, 3, 3);
        filterPanelField4Label.Name = "filterPanelField4Label";
        filterPanelField4Label.Size = new Size(95, 28);
        filterPanelField4Label.TabIndex = 8;
        filterPanelField4Label.Text = "Đến ngày";
        //
        // filterPanelField4
        //
        filterPanelField4.CustomFormat = "dd/MM/yyyy";
        filterPanelField4.Dock = DockStyle.Top;
        filterPanelField4.Format = DateTimePickerFormat.Custom;
        filterPanelField4.Location = new Point(11, 409);
        filterPanelField4.Margin = new Padding(3, 3, 8, 6);
        filterPanelField4.Name = "filterPanelField4";
        filterPanelField4.Size = new Size(217, 34);
        filterPanelField4.TabIndex = 9;
        //
        // content
        //
        content.ColumnCount = 1;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20F));
        content.Controls.Add(toolbar, 0, 0);
        content.Controls.Add(viewTabs, 0, 1);
        content.Dock = DockStyle.Fill;
        content.Location = new Point(253, 3);
        content.Name = "content";
        content.RowCount = 2;
        content.RowStyles.Add(new RowStyle());
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.Size = new Size(826, 506);
        content.TabIndex = 1;
        //
        // toolbar
        //
        toolbar.AutoSize = true;
        toolbar.Controls.Add(reloadButton);
        toolbar.Controls.Add(addButton);
        toolbar.Controls.Add(renewButton);
        toolbar.Controls.Add(endButton);
        toolbar.Controls.Add(helpButton);
        toolbar.Dock = DockStyle.Fill;
        toolbar.Location = new Point(3, 3);
        toolbar.Name = "toolbar";
        toolbar.Size = new Size(820, 44);
        toolbar.TabIndex = 0;
        //
        // reloadButton
        //
        reloadButton.AutoSize = true;
        reloadButton.Location = new Point(3, 3);
        reloadButton.MinimumSize = new Size(70, 34);
        reloadButton.Name = "reloadButton";
        reloadButton.Size = new Size(75, 38);
        reloadButton.TabIndex = 0;
        reloadButton.Tag = "pending";
        reloadButton.Text = "Tải lại";
        //
        // addButton
        //
        addButton.AutoSize = true;
        addButton.Location = new Point(84, 3);
        addButton.MinimumSize = new Size(70, 34);
        addButton.Name = "addButton";
        addButton.Size = new Size(146, 38);
        addButton.TabIndex = 1;
        addButton.Tag = "pending";
        addButton.Text = "Tạo hợp đồng";
        //
        // renewButton
        //
        renewButton.AutoSize = true;
        renewButton.Location = new Point(236, 3);
        renewButton.MinimumSize = new Size(70, 34);
        renewButton.Name = "renewButton";
        renewButton.Size = new Size(88, 38);
        renewButton.TabIndex = 2;
        renewButton.Tag = "pending";
        renewButton.Text = "Gia hạn";
        //
        // endButton
        //
        endButton.AutoSize = true;
        endButton.Location = new Point(330, 3);
        endButton.MinimumSize = new Size(70, 34);
        endButton.Name = "endButton";
        endButton.Size = new Size(108, 38);
        endButton.TabIndex = 3;
        endButton.Tag = "pending";
        endButton.Text = "Chấm dứt";
        //
        // helpButton
        //
        helpButton.AutoSize = true;
        helpButton.Location = new Point(444, 3);
        helpButton.MinimumSize = new Size(70, 34);
        helpButton.Name = "helpButton";
        helpButton.Size = new Size(75, 38);
        helpButton.TabIndex = 4;
        helpButton.Tag = "help";
        helpButton.Text = "?";
        //
        // viewTabs
        //
        viewTabs.Controls.Add(tab0);
        viewTabs.Dock = DockStyle.Fill;
        viewTabs.Location = new Point(3, 53);
        viewTabs.Name = "viewTabs";
        viewTabs.SelectedIndex = 0;
        viewTabs.Size = new Size(820, 450);
        viewTabs.TabIndex = 1;
        //
        // tab0
        //
        tab0.Controls.Add(grid0);
        tab0.Location = new Point(4, 37);
        tab0.Name = "tab0";
        tab0.Padding = new Padding(8);
        tab0.Size = new Size(812, 409);
        tab0.TabIndex = 0;
        tab0.Text = "Hợp đồng";
        //
        // grid0
        //
        grid0.Dock = DockStyle.Fill;
        grid0.Location = new Point(8, 8);
        grid0.Name = "grid0";
        grid0.Size = new Size(796, 393);
        grid0.TabIndex = 0;
        //
        // FrmContracts
        //
        AutoScaleDimensions = new SizeF(144F, 144F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 248, 248);
        ClientSize = new Size(1120, 670);
        Controls.Add(root);
        Font = new Font("Segoe UI", 10F);
        MinimumSize = new Size(620, 480);
        Name = "FrmContracts";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "F23 · Hợp đồng";
        root.ResumeLayout(false);
        root.PerformLayout();
        body.ResumeLayout(false);
        filterViewport.ResumeLayout(false);
        filterViewport.PerformLayout();
        filterPanel.ResumeLayout(false);
        filterPanel.PerformLayout();
        content.ResumeLayout(false);
        content.PerformLayout();
        toolbar.ResumeLayout(false);
        toolbar.PerformLayout();
        viewTabs.ResumeLayout(false);
        tab0.ResumeLayout(false);
        ResumeLayout(false);
    }
}
