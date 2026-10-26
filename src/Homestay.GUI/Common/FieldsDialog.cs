using System.ComponentModel;
using System.Globalization;

namespace Homestay.GUI.Common;

public enum FieldKind { Text, Multiline, Integer, Date, Choice }
public sealed record FieldSpec(string Key, string Label, FieldKind Kind = FieldKind.Text,
    object? Initial = null, bool Required = true, Choice[]? Choices = null, int MaxLength = 500);

public partial class FieldsDialog : ScreenForm
{
    private readonly Dictionary<string, Control> inputs = [];
    private FieldSpec[] specs = [];
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyDictionary<string, object?> Values { get; private set; } = new Dictionary<string,object?>();
    public FieldsDialog()
    {
        InitializeComponent();
        saveButton.Click += (_, _) => SaveValues();
        cancelButton.Click += (_, _) => { DialogResult=DialogResult.Cancel;Close(); };
        AcceptButton=saveButton;CancelButton=cancelButton;
    }
    public FieldsDialog(string title, params FieldSpec[] specs) : this()
    {
        Text=title;this.specs=specs;
        for(var i=0;i<specs.Length;i++)
        {
            var spec=specs[i];
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label=new Label{Text=spec.Label,AutoSize=true,Margin=new Padding(4,12,4,4)};
            Control input;
            if(spec.Kind==FieldKind.Choice)
            {
                var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember=nameof(Choice.Label)};
                combo.Items.AddRange(spec.Choices??[]);
                combo.SelectedIndex=Array.FindIndex(spec.Choices??[],c=>Equals(c.Value,spec.Initial));
                input=combo;
            }
            else
            {
                var text = spec.Initial is DateOnly date ? date.ToString("dd/MM/yyyy",UiFormat.Vietnamese)
                    : Convert.ToString(spec.Initial,CultureInfo.InvariantCulture)??"";
                var box=new TextBox{Text=text,MaxLength=spec.MaxLength};
                if(spec.Kind==FieldKind.Multiline){box.Multiline=true;box.Height=100;}
                if(spec.Kind==FieldKind.Date)box.PlaceholderText="dd/MM/yyyy";
                input=box;
            }
            input.Name=spec.Key;input.AccessibleName=spec.Label;input.Dock=DockStyle.Top;
            input.TabIndex=i;input.Margin=new Padding(4,3,12,4);
            inputs.Add(spec.Key,input);
            fieldsPanel.Controls.Add(label,0,i*2);fieldsPanel.Controls.Add(input,0,i*2+1);
        }
    }
    private void SaveValues()
    {
        var values=new Dictionary<string,object?>();
        foreach(var spec in specs)
        {
            var input=inputs[spec.Key];
            object? value=input is ComboBox combo ? (combo.SelectedItem as Choice)?.Value : input.Text.Trim();
            if(spec.Required && (value is null || value is string s && string.IsNullOrWhiteSpace(s)))
            { MessageBox.Show(this,"Vui lòng nhập "+spec.Label.ToLowerInvariant()+".",Text);input.Focus();return; }
            if(spec.Kind==FieldKind.Integer)
            {
                if(!int.TryParse(input.Text,CultureInfo.InvariantCulture,out var number))
                { MessageBox.Show(this,spec.Label+" phải là số nguyên.",Text);input.Focus();return; }
                value=number;
            }
            if(spec.Kind==FieldKind.Date)
            {
                if(!DateOnly.TryParseExact(input.Text,"dd/MM/yyyy",UiFormat.Vietnamese,DateTimeStyles.None,out var date))
                { MessageBox.Show(this,spec.Label+" phải theo dạng dd/MM/yyyy.",Text);input.Focus();return; }
                value=date;
            }
            values.Add(spec.Key,value);
        }
        Values=values;DialogResult=DialogResult.OK;Close();
    }
}
