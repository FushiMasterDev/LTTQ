#nullable enable
namespace Homestay.GUI.Common;
partial class FieldsDialog
{
    private System.ComponentModel.IContainer? components;
    private TableLayoutPanel root = null!;
    private Panel viewport = null!;
    private TableLayoutPanel fieldsPanel = null!;
    private FlowLayoutPanel actions = null!;
    private Label summaryLabel = null!;
    private Label validationLabel = null!;
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
        summaryLabel=new Label{AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(4,4,4,12),Visible=false};
        validationLabel=new Label{AutoSize=true,Dock=DockStyle.Fill,ForeColor=Color.Firebrick,Padding=new Padding(4,8,4,8),Visible=false};
        cancelButton=new Button();
        saveButton=new Button();
        Text="Nhập thông tin";
        root.Dock=DockStyle.Fill;
        root.RowCount=4;
        root.ColumnCount=1;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.Padding=new Padding(16);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        viewport.Dock=DockStyle.Fill;
        viewport.AutoScroll=true;
        fieldsPanel.Dock=DockStyle.Top;
        fieldsPanel.AutoSize=true;
        fieldsPanel.ColumnCount=1;
        fieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
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
        root.Controls.Add(summaryLabel,0,0);
        root.Controls.Add(viewport,0,1);
        root.Controls.Add(validationLabel,0,2);
        root.Controls.Add(actions,0,3);
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
