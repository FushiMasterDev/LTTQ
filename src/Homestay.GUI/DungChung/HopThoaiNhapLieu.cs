using System.ComponentModel;
using System.Globalization;
using Homestay.DTO.Common;

namespace Homestay.GUI.Common;

/// <summary>Kiểu ô nhập trong hộp thoại nhập liệu: chữ, nhiều dòng, số, ngày, lựa chọn, tiền, giờ…</summary>
public enum FieldKind { Text, Multiline, Integer, Date, Choice, Confirmation, Money, Decimal, Time, Lookup }
/// <summary>Mô tả một ô nhập: khóa, nhãn, kiểu, bắt buộc, giá trị mặc định, lựa chọn.</summary>
public sealed record FieldSpec(string Key, string Label, FieldKind Kind = FieldKind.Text,
    object? Initial = null, bool Required = true, Choice[]? Choices = null, int MaxLength = 500,
    decimal? Minimum = null, decimal? Maximum = null);

/// <summary>Hộp thoại nhập liệu dựng tự động từ danh sách ô nhập, có kiểm tra dữ liệu trước khi đóng.</summary>
public partial class FieldsDialog : ScreenForm
{
    private readonly Dictionary<string, Control> inputs = [];
    private FieldSpec[] specs = [];
    private bool previewOnly;
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyDictionary<string, object?> Values { get; private set; } = new Dictionary<string,object?>();
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<IReadOnlyDictionary<string,object?>,string?>? ValidateValues { get; set; }
    public FieldsDialog()
    {
        InitializeComponent();
        saveButton.Click += (_, _) => SaveValues();
        cancelButton.Click += (_, _) => { DialogResult=DialogResult.Cancel;Close(); };
        AcceptButton=saveButton;CancelButton=cancelButton;
        Resize+=(_,_)=>{
            var width=Math.Max(180,ClientSize.Width-Padding.Horizontal-48);
            summaryLabel.MaximumSize=validationLabel.MaximumSize=new Size(width,0);
            FitLabels();
        };
    }
    public FieldsDialog(string title, params FieldSpec[] specs) : this()
    {
        Text=title;this.specs=specs;
        for(var i=0;i<specs.Length;i++)
        {
            var spec=specs[i];
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fieldsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label=new Label{Text=spec.Label+(spec.Required?" *":""),AutoSize=true,Margin=new Padding(4,12,4,4),MaximumSize=new Size(620,0)};
            Control input;
            if(spec.Kind==FieldKind.Choice)
            {
                var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DisplayMember=nameof(Choice.Label)};
                combo.Items.AddRange(spec.Choices??[]);
                combo.SelectedIndex=Array.FindIndex(spec.Choices??[],c=>Equals(c.Value,spec.Initial));
                input=combo;
            }
            else if(spec.Kind==FieldKind.Lookup){
                var picker=new SearchPicker();
                picker.SetItems((spec.Choices??[]).Where(c=>c.Value is int).Select(c=>new Lookup{Id=(int)c.Value!,Name=c.Label}));
                picker.SelectId(spec.Initial as int?);input=picker;
            }
            else if(spec.Kind==FieldKind.Confirmation){
                input=new CheckBox{AutoSize=true,Text=spec.Label,Checked=spec.Initial is true};label.Text="Xác nhận"+(spec.Required?" *":"");
            }
            else if(spec.Kind is FieldKind.Money or FieldKind.Decimal){
                var number=new NumericUpDown{Minimum=spec.Minimum??0,Maximum=spec.Maximum??999999999999999999m,DecimalPlaces=spec.Kind==FieldKind.Money?0:2,ThousandsSeparator=true};
                number.Value=Math.Clamp(spec.Initial is null?number.Minimum:Convert.ToDecimal(spec.Initial,CultureInfo.InvariantCulture),number.Minimum,number.Maximum);input=number;
            }
            else if(spec.Kind==FieldKind.Time){
                var time=spec.Initial is TimeOnly t?t:new TimeOnly(0,0);
                input=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm",ShowUpDown=true,Value=DateTime.Today.Add(time.ToTimeSpan())};
            }
            else
            {
                var text = spec.Initial is DateOnly date ? date.ToString("dd/MM/yyyy",UiFormat.Vietnamese)
                    : Convert.ToString(spec.Initial,CultureInfo.InvariantCulture)??"";
                var box=new TextBox{Text=text,MaxLength=spec.MaxLength};
                if(spec.Kind==FieldKind.Multiline){box.Multiline=true;box.Height=100;box.ScrollBars=ScrollBars.Vertical;}
                if(spec.Kind==FieldKind.Date)box.PlaceholderText="dd/MM/yyyy";
                input=box;
            }
            input.Name=spec.Key;input.AccessibleName=spec.Label;input.Dock=DockStyle.Top;
            input.TabIndex=i;input.Margin=new Padding(4,3,12,4);
            inputs.Add(spec.Key,input);
            fieldsPanel.Controls.Add(label,0,i*2);fieldsPanel.Controls.Add(input,0,i*2+1);
        }
    }
    private void FitLabels(){
        var width=Math.Max(140,viewport.ClientSize.Width-32);
        foreach(var label in fieldsPanel.Controls.OfType<Label>())label.MaximumSize=new Size(width,0);
        foreach(var box in fieldsPanel.Controls.OfType<CheckBox>())box.MaximumSize=new Size(width,0);
    }
    protected override void OnShown(EventArgs e){
        base.OnShown(e);
        var available=Screen.FromControl(this).WorkingArea;
        var bounded=new Size(Math.Min(Width,available.Width),Math.Min(Height,available.Height));
        if(bounded!=Size){Size=bounded;CenterToScreen();}
        FitLabels();
    }
    public void SetSummary(string text){summaryLabel.MaximumSize=new Size(Math.Max(180,ClientSize.Width-48),0);summaryLabel.Text=text;summaryLabel.Visible=!string.IsNullOrWhiteSpace(text);}
    public void SetPreviewMode(){
        previewOnly=true;saveButton.Text="Kiểm tra biểu mẫu";cancelButton.Text="Đóng";
        SetSummary("BIỂU MẪU ĐỢT 4 — CHƯA LƯU DỮ LIỆU. Phần nhập liệu này đang được chuẩn bị. Dấu * là bắt buộc.");
    }
    private void Invalid(Control? input,string message){validationLabel.Text=message;validationLabel.ForeColor=Color.Firebrick;validationLabel.Visible=true;input?.Focus();}
    private void SaveValues()
    {
        var values=new Dictionary<string,object?>();
        validationLabel.Visible=false;
        foreach(var spec in specs)
        {
            var input=inputs[spec.Key];
            object? value=input switch{
                ComboBox combo=>(combo.SelectedItem as Choice)?.Value,
                SearchPicker picker=>picker.SelectedItem?.Id,
                CheckBox check=>check.Checked,
                NumericUpDown number=>number.Value,
                DateTimePicker time=>new TimeOnly(time.Value.Hour,time.Value.Minute),
                _=>input.Text.Trim()
            };
            if(spec.Kind==FieldKind.Confirmation&&spec.Required&&value is not true){Invalid(input,"Vui lòng xác nhận: "+spec.Label);return;}
            if(spec.Required && (value is null || value is string s && string.IsNullOrWhiteSpace(s)))
            { Invalid(input,"Vui lòng nhập "+spec.Label.ToLowerInvariant()+".");return; }
            if(!spec.Required&&spec.Kind is FieldKind.Integer or FieldKind.Date&&value is string empty&&string.IsNullOrWhiteSpace(empty)){values.Add(spec.Key,null);continue;}
            if(spec.Kind==FieldKind.Integer)
            {
                if(!int.TryParse(input.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number)||
                    spec.Minimum is decimal min&&number<min||spec.Maximum is decimal max&&number>max)
                { Invalid(input,spec.Label+" phải là số nguyên"+(spec.Minimum is decimal low?" từ "+low:"")+(spec.Maximum is decimal high?" đến "+high:"")+".");return; }
                value=number;
            }
            if(spec.Kind==FieldKind.Date)
            {
                if(!DateOnly.TryParseExact(input.Text,"dd/MM/yyyy",UiFormat.Vietnamese,DateTimeStyles.None,out var date))
                { Invalid(input,spec.Label+" phải theo dạng dd/MM/yyyy.");return; }
                value=date;
            }
            values.Add(spec.Key,value);
        }
        if(ValidateValues?.Invoke(values) is string error&&!string.IsNullOrWhiteSpace(error)){Invalid(null,error);return;}
        Values=values;
        if(previewOnly){validationLabel.Text="Biểu mẫu hợp lệ. Chưa lưu vào database.";validationLabel.ForeColor=Color.DarkGreen;validationLabel.Visible=true;return;}
        DialogResult=DialogResult.OK;Close();
    }
}