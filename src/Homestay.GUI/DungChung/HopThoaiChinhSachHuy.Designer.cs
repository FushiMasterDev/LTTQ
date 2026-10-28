#nullable enable
namespace Homestay.GUI.Common;
partial class CancellationPolicyDialog
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel root = null!;
    private Label nameLabel = null!;
    private TextBox nameBox = null!;
    private Label scopeLabel = null!;
    private ComboBox scopeBox = null!;
    private CheckBox activateBox = null!;
    private DataGridView rulesGrid = null!;
    private FlowLayoutPanel actions = null!;
    private Button cancelButton = null!;
    private Button saveButton = null!;
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
        nameLabel=new Label();
        nameBox=new TextBox();
        scopeLabel=new Label();
        scopeBox=new ComboBox();
        activateBox=new CheckBox();
        rulesGrid=new DataGridView();
        actions=new FlowLayoutPanel();
        cancelButton=new Button();
        saveButton=new Button();
        Text="Tạo phiên bản chính sách hủy";
        root.Dock=DockStyle.Fill;
        root.Padding=new Padding(20);
        root.RowCount=7;
        root.ColumnCount=1;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        nameLabel.Text="Tên chính sách";
        nameLabel.AutoSize=true;
        nameBox.Dock=DockStyle.Fill;
        nameBox.MaxLength=100;
        scopeLabel.Text="Phạm vi";
        scopeLabel.AutoSize=true;
        scopeBox.DropDownStyle=ComboBoxStyle.DropDownList;
        scopeBox.Dock=DockStyle.Fill;
        activateBox.Text="Áp dụng phiên bản mới";
        activateBox.Checked=true;
        activateBox.AutoSize=true;
        rulesGrid.Dock=DockStyle.Fill;
        rulesGrid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        rulesGrid.AllowUserToAddRows=true;
        rulesGrid.AllowUserToDeleteRows=true;
        actions.AutoSize=true;
        actions.Dock=DockStyle.Fill;
        cancelButton.Text="Bỏ qua";
        cancelButton.AutoSize=true;
        saveButton.Name="saveButton";
        saveButton.Text="Tạo phiên bản";
        saveButton.AutoSize=true;
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(saveButton);
        root.Controls.Add(nameLabel,0,0);
        root.Controls.Add(nameBox,0,1);
        root.Controls.Add(scopeLabel,0,2);
        root.Controls.Add(scopeBox,0,3);
        root.Controls.Add(activateBox,0,4);
        root.Controls.Add(rulesGrid,0,5);
        root.Controls.Add(actions,0,6);
        Controls.Add(root);
        AutoScaleDimensions=new SizeF(96,96);
        AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(760,580);
        MinimumSize=new Size(520,400);
        Font=new Font("Segoe UI",10);
        StartPosition=FormStartPosition.CenterParent;
        ResumeLayout(false);
        PerformLayout();
    }
}
