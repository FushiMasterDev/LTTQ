using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Common;

/// <summary>Lưới dữ liệu tự tải lại và giữ dòng đang chọn khi dữ liệu thay đổi.</summary>
internal sealed class LiveGrid<T>:IDisposable where T:class
{
    private readonly PagedGrid grid;private readonly AsyncLoader loader=new();
    private readonly Func<Func<PageRequest,CancellationToken,Task<PagedResult<T>>>> snapshot;
    private readonly Func<T,object?[]> map;private IReadOnlyList<T> rows=[];
    public event Action? Changed;
    public T? Current=>grid.Grid.SelectedRows.Count==1&&grid.Grid.SelectedRows[0].Index is var index&&index>=0&&index<rows.Count?rows[index]:null;
    public T? AtRow(int index)=>index>=0&&index<rows.Count?rows[index]:null;
    public T Selected=>Current??throw new BusinessException("Vui lòng chọn một dòng trong danh sách.");
    public LiveGrid(PagedGrid grid,Func<Func<PageRequest,CancellationToken,Task<PagedResult<T>>>> snapshot,Func<T,object?[]> map){
        this.grid=grid;this.snapshot=snapshot;this.map=map;
        grid.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);
        grid.Grid.SelectionChanged+=(_,_)=>Changed?.Invoke();
        grid.ExportFilteredAsync=async ct=>{
            var read=snapshot();var q=grid.Query with{PageNumber=1,PageSize=100};var all=new List<T>();
            while(true){var page=await read(q,ct);all.AddRange(page.Items);if(page.Items.Count==0||all.Count>=page.TotalCount)break;q=q with{PageNumber=q.PageNumber+1};}
            return EmployeeUi.Table(grid,all.Select(map));
        };
    }
    public async Task LoadAsync(PageRequest? request=null){
        rows=[];Changed?.Invoke();var read=snapshot();
        try{await loader.LoadAsync(ct=>read(request??grid.Query,ct),page=>{rows=page.Items;grid.SetPage(page,map);Changed?.Invoke();});}
        catch(Exception e){if(!grid.IsDisposed)ErrorHandler.Show(e,grid.FindForm());}
    }
    public void Dispose()=>loader.Dispose();
}
/// <summary>Hàm tiện ích nhỏ cho các màn hình dùng LiveGrid: hiển thị trạng thái, điền dữ liệu, đọc ghi chú.</summary>
internal static class LiveUi
{
    public static string Status(object value)=>value.ToString() switch{
        "Approve"=>"Duyệt hồ sơ","Submit"=>"Gửi duyệt","Return"=>"Trả lại","Suspend"=>"Tạm ngưng","Reopen"=>"Mở lại","End"=>"Kết thúc","Pending"=>"Chờ duyệt","Approved"=>"Đã duyệt","Suspended"=>"Tạm ngưng","Rejected"=>"Từ chối","Draft"=>"Nháp","PendingReview"=>"Chờ duyệt","Active"=>"Đang hoạt động","Ended"=>"Đã kết thúc","Expired"=>"Hết hạn","Terminated"=>"Chấm dứt","Open"=>"Chưa bắt đầu","InProgress"=>"Đang làm","Completed"=>"Hoàn thành","Done"=>"Hoàn thành","Cancelled"=>"Đã hủy","Low"=>"Thấp","Medium"=>"Vừa","High"=>"Cao","Urgent"=>"Khẩn","BusinessLicense"=>"Giấy phép kinh doanh","FireSafety"=>"PCCC","PropertyPhoto"=>"Ảnh cơ sở","Other"=>"Khác","Maintenance"=>"Bảo trì","OtherChannel"=>"Khách kênh khác","All"=>"Mọi ngày","Holiday"=>"Ngày lễ",_=>value.ToString()??""};
    public static void Activate(FlowLayoutPanel toolbar,Label notice,params Button[] buttons){
        notice.Visible=false;foreach(var b in buttons)b.Tag=null;
        foreach(var c in toolbar.Controls.Cast<Control>().Where(c=>Equals(c.Tag,"layout-editor")).ToArray()){toolbar.Controls.Remove(c);c.Dispose();}
    }
    public static void EnumFilter<T>(ComboBox box)where T:struct,Enum{
        box.Items.Clear();box.DisplayMember=nameof(Choice.Label);box.Items.Add(new Choice(null,"Tất cả"));box.Items.AddRange(Enum.GetValues<T>().Select(e=>new Choice(e,Status(e))).ToArray());box.SelectedIndex=0;
    }
    public static T? EnumValue<T>(ComboBox box)where T:struct=>(box.SelectedItem as Choice)?.Value as T?;
    public static string Text(FieldsDialog d,string key)=>Convert.ToString(d.Values[key])??"";
    public static string? Optional(FieldsDialog d,string key)=>string.IsNullOrWhiteSpace(Text(d,key))?null:Text(d,key);
    public static int Id(FieldsDialog d,string key)=>(int)d.Values[key]!;
    public static void Fill(FieldsDialog d,string key,object? value){
        var c=d.Controls.Find(key,true).FirstOrDefault();if(c is null)return;
        if(c is SearchPicker picker)picker.SelectId(value as int?);
        else if(c is DateTimePicker time&&value is TimeOnly t)time.Value=DateTime.Today.Add(t.ToTimeSpan());
        else if(c is NumericUpDown number)number.Value=Convert.ToDecimal(value);
        else c.Text=value is DateOnly date?date.ToString("dd/MM/yyyy"):Convert.ToString(value)??"";
    }
    public static string? Note(IWin32Window owner,string title,bool required=true){using var d=new FieldsDialog(title,new FieldSpec("note","Lý do / ghi chú",FieldKind.Multiline,Required:required));return d.ShowDialog(owner)==DialogResult.OK?Text(d,"note"):null;}
}