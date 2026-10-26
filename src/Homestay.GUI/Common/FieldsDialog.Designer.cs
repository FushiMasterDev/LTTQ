#nullable enable
namespace Homestay.GUI.Common;
partial class FieldsDialog
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel root = null!;
    private Panel viewport = null!;
    private TableLayoutPanel fieldsPanel = null!;
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
        viewport=new Panel();
        fieldsPanel=new TableLayoutPanel();
        actions=new FlowLayoutPanel();
        cancelButton=new Button();
        saveButton=new Button();
        Text="Nhập thông tin";
        root.Dock=DockStyle.Fill;
        root.RowCount=2;
        root.ColumnCount=1;
        root.Padding=new Padding(16);
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        viewport.Dock=DockStyle.Fill;
        viewport.AutoScroll=true;
        fieldsPanel.Dock=DockStyle.Top;
        fieldsPanel.AutoSize=true;
        fieldsPanel.ColumnCount=1;
        viewport.Controls.Add(fieldsPanel);
        actions.Dock=DockStyle.Fill;
        actions.AutoSize=true;
        cancelButton.Text="Bỏ qua";
        cancelButton.AutoSize=true;
        saveButton.Name="saveButton";
        saveButton.Text="Lưu";
        saveButton.AutoSize=true;
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(saveButton);
        root.Controls.Add(viewport,0,0);
        root.Controls.Add(actions,0,1);
        Controls.Add(root);
        AutoScaleDimensions=new SizeF(96,96);
        AutoScaleMode=AutoScaleMode.Dpi;
        ClientSize=new Size(700,580);
        MinimumSize=new Size(520,400);
        Font=new Font("Segoe UI",10);
        StartPosition=FormStartPosition.CenterParent;
        ResumeLayout(false);
        PerformLayout();
    }
}
