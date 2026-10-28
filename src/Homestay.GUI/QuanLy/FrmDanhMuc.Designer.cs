#nullable enable
namespace Homestay.GUI.Manager;

partial class FrmCatalog
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
    private Button editButton = null!;
    private Button toggleButton = null!;
    private Panel filterViewport = null!;
    private TableLayoutPanel filterPanel = null!;
    private Label filterPanelField0Label = null!;
    private TextBox filterPanelField0 = null!;
    private Label filterPanelField1Label = null!;
    private ComboBox filterPanelField1 = null!;
    private TabControl viewTabs = null!;
    private TabPage tab0 = null!;
    private Common.PagedGrid grid0 = null!;
    private TabPage tab1 = null!;
    private Common.PagedGrid grid1 = null!;
    private TabPage tab2 = null!;
    private Common.PagedGrid grid2 = null!;
    private TabPage tab3 = null!;
    private Common.PagedGrid grid3 = null!;
    private TabPage tab4 = null!;
    private Common.PagedGrid grid4 = null!;
    private TabPage tab5 = null!;
    private Common.PagedGrid grid5 = null!;
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
        filterPanelField1 = new ComboBox();
        content = new TableLayoutPanel();
        toolbar = new FlowLayoutPanel();
        reloadButton = new Button();
        addButton = new Button();
        editButton = new Button();
        toggleButton = new Button();
        helpButton = new Button();
        viewTabs = new TabControl();
        tab0 = new TabPage();
        grid0 = new Homestay.GUI.Common.PagedGrid();
        tab1 = new TabPage();
        grid1 = new Homestay.GUI.Common.PagedGrid();
        tab2 = new TabPage();
        grid2 = new Homestay.GUI.Common.PagedGrid();
        tab3 = new TabPage();
        grid3 = new Homestay.GUI.Common.PagedGrid();
        tab4 = new TabPage();
        grid4 = new Homestay.GUI.Common.PagedGrid();
        tab5 = new TabPage();
        grid5 = new Homestay.GUI.Common.PagedGrid();
        root.SuspendLayout();
        body.SuspendLayout();
        filterViewport.SuspendLayout();
        filterPanel.SuspendLayout();
        content.SuspendLayout();
        toolbar.SuspendLayout();
        viewTabs.SuspendLayout();
        tab0.SuspendLayout();
        tab1.SuspendLayout();
        tab2.SuspendLayout();
        tab3.SuspendLayout();
        tab4.SuspendLayout();
        tab5.SuspendLayout();
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
        heading.Size = new Size(447, 48);
        heading.TabIndex = 0;
        heading.Text = "F24 · Danh mục & cấu hình";
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
        filterPanel.Dock = DockStyle.Top;
        filterPanel.Location = new Point(0, 0);
        filterPanel.Name = "filterPanel";
        filterPanel.Padding = new Padding(8);
        filterPanel.RowCount = 5;
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle());
        filterPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        filterPanel.Size = new Size(244, 179);
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
        filterPanelField1Label.Size = new Size(86, 28);
        filterPanelField1Label.TabIndex = 2;
        filterPanelField1Label.Text = "Sử dụng";
        //
        // filterPanelField1
        //
        filterPanelField1.Dock = DockStyle.Top;
        filterPanelField1.DropDownStyle = ComboBoxStyle.DropDownList;
        filterPanelField1.Items.AddRange(new object[] { "Tất cả", "Đang sử dụng", "Ngừng sử dụng" });
        filterPanelField1.Location = new Point(11, 132);
        filterPanelField1.Margin = new Padding(3, 3, 8, 6);
        filterPanelField1.Name = "filterPanelField1";
        filterPanelField1.Size = new Size(217, 36);
        filterPanelField1.TabIndex = 3;
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
        toolbar.Controls.Add(editButton);
        toolbar.Controls.Add(toggleButton);
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
        addButton.Size = new Size(214, 38);
        addButton.TabIndex = 1;
        addButton.Tag = "pending";
        addButton.Text = "Thêm / phiên bản mới";
        //
        // editButton
        //
        editButton.AutoSize = true;
        editButton.Location = new Point(304, 3);
        editButton.MinimumSize = new Size(70, 34);
        editButton.Name = "editButton";
        editButton.Size = new Size(75, 38);
        editButton.TabIndex = 2;
        editButton.Tag = "pending";
        editButton.Text = "Sửa";
        //
        // toggleButton
        //
        toggleButton.AutoSize = true;
        toggleButton.Location = new Point(385, 3);
        toggleButton.MinimumSize = new Size(70, 34);
        toggleButton.Name = "toggleButton";
        toggleButton.Size = new Size(173, 38);
        toggleButton.TabIndex = 3;
        toggleButton.Tag = "pending";
        toggleButton.Text = "Ngừng / dùng lại";
        //
        // helpButton
        //
        helpButton.AutoSize = true;
        helpButton.Location = new Point(564, 3);
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
        viewTabs.Controls.Add(tab1);
        viewTabs.Controls.Add(tab2);
        viewTabs.Controls.Add(tab3);
        viewTabs.Controls.Add(tab4);
        viewTabs.Controls.Add(tab5);
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
        tab0.Text = "Khu vực";
        //
        // grid0
        //
        grid0.Dock = DockStyle.Fill;
        grid0.Location = new Point(8, 8);
        grid0.Name = "grid0";
        grid0.Size = new Size(796, 393);
        grid0.TabIndex = 0;
        //
        // tab1
        //
        tab1.Controls.Add(grid1);
        tab1.Location = new Point(4, 34);
        tab1.Name = "tab1";
        tab1.Padding = new Padding(8);
        tab1.Size = new Size(192, 62);
        tab1.TabIndex = 1;
        tab1.Text = "Tiện nghi";
        //
        // grid1
        //
        grid1.Dock = DockStyle.Fill;
        grid1.Location = new Point(8, 8);
        grid1.Name = "grid1";
        grid1.Size = new Size(176, 46);
        grid1.TabIndex = 0;
        //
        // tab2
        //
        tab2.Controls.Add(grid2);
        tab2.Location = new Point(4, 34);
        tab2.Name = "tab2";
        tab2.Padding = new Padding(8);
        tab2.Size = new Size(192, 62);
        tab2.TabIndex = 2;
        tab2.Text = "Danh mục phòng";
        //
        // grid2
        //
        grid2.Dock = DockStyle.Fill;
        grid2.Location = new Point(8, 8);
        grid2.Name = "grid2";
        grid2.Size = new Size(176, 46);
        grid2.TabIndex = 0;
        //
        // tab3
        //
        tab3.Controls.Add(grid3);
        tab3.Location = new Point(4, 34);
        tab3.Name = "tab3";
        tab3.Padding = new Padding(8);
        tab3.Size = new Size(192, 62);
        tab3.TabIndex = 3;
        tab3.Text = "Ngày lễ";
        //
        // grid3
        //
        grid3.Dock = DockStyle.Fill;
        grid3.Location = new Point(8, 8);
        grid3.Name = "grid3";
        grid3.Size = new Size(176, 46);
        grid3.TabIndex = 0;
        //
        // tab4
        //
        tab4.Controls.Add(grid4);
        tab4.Location = new Point(4, 34);
        tab4.Name = "tab4";
        tab4.Padding = new Padding(8);
        tab4.Size = new Size(192, 62);
        tab4.TabIndex = 4;
        tab4.Text = "Chính sách hủy chung";
        //
        // grid4
        //
        grid4.Dock = DockStyle.Fill;
        grid4.Location = new Point(8, 8);
        grid4.Name = "grid4";
        grid4.Size = new Size(176, 46);
        grid4.TabIndex = 0;
        //
        // tab5
        //
        tab5.Controls.Add(grid5);
        tab5.Location = new Point(4, 34);
        tab5.Name = "tab5";
        tab5.Padding = new Padding(8);
        tab5.Size = new Size(192, 62);
        tab5.TabIndex = 5;
        tab5.Text = "Tham số";
        //
        // grid5
        //
        grid5.Dock = DockStyle.Fill;
        grid5.Location = new Point(8, 8);
        grid5.Name = "grid5";
        grid5.Size = new Size(176, 46);
        grid5.TabIndex = 0;
        //
        // FrmCatalog
        //
        AutoScaleDimensions = new SizeF(144F, 144F);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(245, 248, 248);
        ClientSize = new Size(1120, 670);
        Controls.Add(root);
        Font = new Font("Segoe UI", 10F);
        MinimumSize = new Size(620, 480);
        Name = "FrmCatalog";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "F24 · Danh mục & cấu hình";
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
        tab1.ResumeLayout(false);
        tab2.ResumeLayout(false);
        tab3.ResumeLayout(false);
        tab4.ResumeLayout(false);
        tab5.ResumeLayout(false);
        ResumeLayout(false);
    }
}
