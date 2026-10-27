using Homestay.DTO.Common;
using System.ComponentModel;

namespace Homestay.GUI.Common;

[DefaultEvent(nameof(SelectionChanged))]
public sealed class SearchPicker : UserControl
{
    private readonly TextBox search=new(){Dock=DockStyle.Fill,PlaceholderText="Gõ tên không dấu để tìm…",Margin=Padding.Empty};
    private readonly ComboBox choices=new(){Dock=DockStyle.Bottom,DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly Button clearSelection=new(){Text="×",AccessibleName="Bỏ chọn",Name="clearSelection",Dock=DockStyle.Fill,Size=new Size(28,23),Margin=Padding.Empty};
    private List<Lookup> all=[];
    public event EventHandler? SelectionChanged;
    [Browsable(false),DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Lookup? SelectedItem=>choices.SelectedItem as Lookup;
    public SearchPicker()
    {
        Height=66;MinimumSize=new Size(100,66);
        var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RowCount=1,Margin=Padding.Empty,Padding=Padding.Empty};
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,28));
        top.Controls.Add(search,0,0);top.Controls.Add(clearSelection,1,0);
        Controls.Add(choices);Controls.Add(top);
        choices.DisplayMember=nameof(Lookup.Name);
        search.TextChanged+=(_,_)=>Filter();
        choices.SelectedIndexChanged+=(_,_)=>SelectionChanged?.Invoke(this,EventArgs.Empty);
        clearSelection.Click+=(_,_)=>SelectId(null);
    }
    public void SetItems(IEnumerable<Lookup> items){all=items.ToList();Filter();}
    public void SelectId(int? id)
    {
        search.Clear();
        choices.SelectedIndex=all.FindIndex(x=>x.Id==id);
    }
    private void Filter()
    {
        var key=SelectedItem?.Id;var query=UiFormat.SearchKey(search.Text);
        var results=all.Where(x=>UiFormat.SearchKey(x.Name).Contains(query)).ToList();
        choices.BeginUpdate();
        try{
            // Items avoid CurrencyManager forcing the first entry when the selection is cleared.
            choices.Items.Clear();choices.Items.AddRange(results.Cast<object>().ToArray());
            choices.SelectedIndex=results.FindIndex(x=>x.Id==key);
        }finally{choices.EndUpdate();}
    }
}
