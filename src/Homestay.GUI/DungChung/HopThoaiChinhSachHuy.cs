using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.GUI.Common;

/// <summary>Hộp thoại nhập chính sách hủy và các mốc hoàn tiền.</summary>
public partial class CancellationPolicyDialog : ScreenForm
{
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public CreateCancellationPolicyRequest? Request { get; private set; }
    public CancellationPolicyDialog()
    {
        InitializeComponent();
        scopeBox.Items.AddRange([new Choice(CancellationScope.All,"Mọi ngày"),new Choice(CancellationScope.Holiday,"Ngày lễ")]);
        scopeBox.DisplayMember=nameof(Choice.Label);scopeBox.SelectedIndex=0;
        rulesGrid.Columns.Add("days","Từ số ngày trước nhận phòng");
        rulesGrid.Columns.Add("percent","Tỷ lệ hoàn (%)");
        rulesGrid.Rows.Add(0,0);
        rulesGrid.DefaultCellStyle.BackColor=Color.LightYellow;
        saveButton.Click+=(_,_)=>Save();
        cancelButton.Click+=(_,_)=>{DialogResult=DialogResult.Cancel;Close();};
        AcceptButton=saveButton;CancelButton=cancelButton;
    }
    private void Save()
    {
        rulesGrid.EndEdit();
        var rules=new List<CancellationRuleItem>();
        foreach(DataGridViewRow row in rulesGrid.Rows)
        {
            if(row.IsNewRow)continue;
            if(!int.TryParse(Convert.ToString(row.Cells[0].Value),out var days) ||
                !int.TryParse(Convert.ToString(row.Cells[1].Value),out var percent))
            { MessageBox.Show(this,"Mỗi mốc cần số ngày và tỷ lệ hoàn là số nguyên.",Text);return; }
            rules.Add(new(days,percent));
        }
        if(string.IsNullOrWhiteSpace(nameBox.Text)){MessageBox.Show(this,"Vui lòng nhập tên chính sách.",Text);return;}
        Request=new CreateCancellationPolicyRequest
        {
            Name=nameBox.Text.Trim(),Scope=(CancellationScope)((Choice)scopeBox.SelectedItem!).Value!,
            Activate=activateBox.Checked,Rules=rules
        };
        DialogResult=DialogResult.OK;Close();
    }
}
