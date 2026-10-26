using Homestay.DTO.Common;
using System.ComponentModel;

namespace Homestay.GUI.Common;

[DefaultEvent(nameof(SelectionChanged))]
public sealed class SearchPicker : UserControl
{
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Gõ tên không dấu để tìm…" };
    private readonly ComboBox choices = new() { Dock = DockStyle.Bottom, DropDownStyle = ComboBoxStyle.DropDownList };
    private List<Lookup> all = [];
    public event EventHandler? SelectionChanged;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Lookup? SelectedItem => choices.SelectedItem as Lookup;
    public SearchPicker()
    {
        Height = 66;
        MinimumSize = new Size(100, 66);
        Controls.Add(choices);
        Controls.Add(search);
        choices.DisplayMember = nameof(Lookup.Name);
        search.TextChanged += (_, _) => Filter();
        choices.SelectedIndexChanged += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetItems(IEnumerable<Lookup> items)
    {
        all = items.ToList();
        Filter();
    }
    private void Filter()
    {
        var key = SelectedItem?.Id;
        var query = UiFormat.SearchKey(search.Text);
        var results = all.Where(x => UiFormat.SearchKey(x.Name).Contains(query)).ToList();
        choices.DataSource = results;
        var index = results.FindIndex(x => Equals(x.Id, key));
        choices.SelectedIndex = index;
    }
}
