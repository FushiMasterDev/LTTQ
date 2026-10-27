using System.Data;
using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Manager;

public partial class FrmCatalog : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<object> all=[];
    private IReadOnlyList<object> shown=[];
    private readonly NumericUpDown holidayYear=new(){Minimum=1900,Maximum=9999,Value=DateTime.Today.Year,Width=90};
    private PagedGrid[] Grids=>[grid0,grid1,grid2,grid3,grid4,grid5];
    private PagedGrid CurrentGrid=>Grids[viewTabs.SelectedIndex];
    public FrmCatalog()
    {
        InitializeComponent();
        HelpText="Chọn tab, tìm tên hoặc khóa rồi Tải lại. Danh mục đã dùng chỉ ngừng sử dụng. Ngày lễ chỉ đổi tên. Chính sách hủy tạo phiên bản mới. Tham số dùng dấu chấm cho số thập phân; giá trị được kiểm tra tại máy chủ.";
        Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmCatalog(AppServices services):this()
    {
        if(!services.Can(Permission.CatalogManage)||!services.Can(Permission.AccountManage))throw new ForbiddenException();
        this.services=services;pendingNotice.Visible=false;
        foreach(var button in new[]{reloadButton,addButton,editButton,toggleButton})button.Tag=null;
        filterPanelField0Label.Text="Tìm tên / khóa";
        filterPanelField1.Items.Clear();
        filterPanelField1.Items.AddRange(new object[]{new Choice(null,"Tất cả"),new Choice(true,"Đang sử dụng"),new Choice(false,"Ngừng sử dụng")});
        filterPanelField1.SelectedIndex=0;
        holidayYear.AccessibleName="Năm ngày lễ";
        toolbar.Controls.Add(new Label{Text="Năm ngày lễ",AutoSize=true,Padding=new Padding(4,8,0,0)});
        toolbar.Controls.Add(holidayYear);
        grid0.SetColumns("Mã","Tên","Tỉnh / thành","Nhóm","Trạng thái");
        grid4.SetColumns("Mã","Tên","Phạm vi","Tạo lúc","Quy tắc ngày / hoàn %","Trạng thái");
        grid5.SetColumns("Khóa","Giá trị","Mô tả","Kiểu / khoảng / định dạng");
        for(var i=0;i<Grids.Length;i++){
            var grid=Grids[i];
            grid.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F24."+i);
            grid.QueryRequested+=(_,e)=>Apply(e.Request);
            grid.DetailRequested+=async(_,_)=>await RunMutationAsync(editButton,EditAsync,ReloadAsync);
            grid.ExportFilteredAsync=ct=>Task.FromResult(ExportTable());
        }
        reloadButton.Click+=async(_,_)=>await ReloadAsync();
        addButton.Click+=async(_,_)=>await RunMutationAsync(addButton,CreateAsync,ReloadAsync);
        editButton.Click+=async(_,_)=>await RunMutationAsync(editButton,EditAsync,ReloadAsync);
        toggleButton.Click+=async(_,_)=>await RunMutationAsync(toggleButton,ToggleAsync,ReloadAsync);
        filterPanelField0.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Apply(CurrentGrid.Query with{PageNumber=1});}};
        filterPanelField1.SelectedIndexChanged+=(_,_)=>Apply(CurrentGrid.Query with{PageNumber=1});
        viewTabs.SelectedIndexChanged+=async(_,_)=>await ReloadAsync();
        Shown+=async(_,_)=>await ReloadAsync();
    }
    private async Task ReloadAsync()
    {
        if(services is null)return;
        var index=viewTabs.SelectedIndex;var year=(int)holidayYear.Value;
        all=[];shown=[]; Apply(new PageRequest{PageSize=CurrentGrid.Query.PageSize});
        addButton.Enabled=index!=5;editButton.Enabled=index!=4;toggleButton.Enabled=index is 0 or 1 or 2 or 4;
        filterPanelField1.Enabled=index is 0 or 1 or 2 or 4;
        holidayYear.Enabled=index==3;
        try{
            await loader.LoadAsync(async ct=>(IReadOnlyList<object>)(index switch{
                0=>(await services.Catalog.ListAreasAsync(ct)).Cast<object>().ToArray(),
                1 or 2=>(await services.Catalog.ListItemsAsync(index==1?CatalogKind.Amenity:CatalogKind.RoomCategory,ct)).Cast<object>().ToArray(),
                3=>(await services.Catalog.ListHolidaysAsync(year,ct)).Cast<object>().ToArray(),
                4=>(await services.Catalog.ListPoliciesAsync(cancellationToken:ct)).Cast<object>().ToArray(),
                _=>(await services.Catalog.ListSettingsAsync(ct)).Cast<object>().ToArray()
            }),result=>{all=result;Apply(new PageRequest{PageSize=CurrentGrid.Query.PageSize});});
        }catch(Exception exception){if(!IsDisposed)ErrorHandler.Show(exception,this);}
    }
    private static bool? Active(object x)=>x switch{AreaItem a=>a.IsActive,CatalogItem c=>c.IsActive,CancellationPolicyItem p=>p.IsActive,_=>null};
    private IEnumerable<object> Filtered()
    {
        var search=UiFormat.SearchKey(filterPanelField0.Text);
        var active=(filterPanelField1.SelectedItem as Choice)?.Value as bool?;
        return all.Where(x=>(active is null||Active(x) is null||Active(x)==active)
            &&UiFormat.SearchKey(string.Join(" ",Map(x))).Contains(search,StringComparison.Ordinal));
    }
    private void Apply(PageRequest query)
    {
        if(services is null||viewTabs.SelectedIndex<0)return;
        var list=Filtered();
        if(query.SortColumn is string column&&column.StartsWith("Column",StringComparison.Ordinal)&&int.TryParse(column[6..],out var i)&&i>=0&&i<CurrentGrid.Grid.Columns.Count)
            list=query.SortDescending?list.OrderByDescending(x=>Map(x)[i],Comparer<object?>.Create(Compare)):list.OrderBy(x=>Map(x)[i],Comparer<object?>.Create(Compare));
        var items=list.ToArray();
        var page=Math.Clamp(query.PageNumber,1,Math.Max(1,(int)Math.Ceiling((double)items.Length/query.PageSize)));
        shown=items.Skip((page-1)*query.PageSize).Take(query.PageSize).ToArray();
        CurrentGrid.SetPage(new PagedResult<object>{Items=shown,TotalCount=items.Length,PageNumber=page,PageSize=query.PageSize},Map);
    }
    private static int Compare(object? a,object? b)=>a is IComparable c&&b?.GetType()==a.GetType()?c.CompareTo(b):StringComparer.CurrentCultureIgnoreCase.Compare(a?.ToString(),b?.ToString());
    private static string Group(AreaGroup group)=>UiNames.AreaGroups().First(c=>Equals(c.Value,group)).Label;
    private static object?[] Map(object x)=>x switch{
        AreaItem a=>[a.Id,a.Name,a.Province,Group(a.AreaGroup),UiNames.Active(a.IsActive)],
        CatalogItem c=>[c.Id,c.Name,UiNames.Active(c.IsActive)],
        HolidayItem h=>[h.Date.ToString("dd/MM/yyyy"),h.Name],
        CancellationPolicyItem p=>[p.Id,p.Name,p.Scope==CancellationScope.All?"Mọi ngày":"Ngày lễ",
            UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(p.CreatedAtUtc,DateTimeKind.Utc))).ToString("dd/MM/yyyy HH:mm"),
            string.Join("; ",p.Rules.Select(r=>r.MinDaysBefore+" ngày: "+r.RefundPercent+"%")),UiNames.Active(p.IsActive)],
        SettingItem s=>[s.Key,s.Value,s.Description,s.ValueType+" "+s.Minimum?.ToString(CultureInfo.InvariantCulture)+" – "+s.Maximum?.ToString(CultureInfo.InvariantCulture)+" "+s.Format],
        _=>throw new InvalidDataException("Dữ liệu không hỗ trợ.")};
    private object Selected()
    {
        if(CurrentGrid.Grid.SelectedRows.Count!=1)throw new InvalidDataException("Vui lòng chọn một dòng.");
        return shown[CurrentGrid.Grid.SelectedRows[0].Index];
    }
    private async Task CreateAsync()
    {
        switch(viewTabs.SelectedIndex){
            case 0: await SaveAreaAsync(null);break;
            case 1:case 2:await SaveNameAsync(null);break;
            case 3:
                using(var d=new FieldsDialog("Thêm ngày lễ",new("date","Ngày",FieldKind.Date,DateOnly.FromDateTime(new DateTime((int)holidayYear.Value,1,1))),new("name","Tên ngày lễ",MaxLength:200))){
                    if(d.ShowDialog(this)!=DialogResult.OK)return;
                    await services!.Catalog.CreateHolidayAsync(new HolidayItem((DateOnly)d.Values["date"]!,(string)d.Values["name"]!));
                }break;
            case 4:
                using(var d=new CancellationPolicyDialog()){
                    if(d.ShowDialog(this)!=DialogResult.OK||d.Request is null)return;
                    await services!.Catalog.CreatePolicyAsync(d.Request);
                }break;
            default:return;
        }
        await ReloadAsync();
    }
    private async Task EditAsync()
    {
        if(viewTabs.SelectedIndex==4){MessageBox.Show(this,"Chọn Thêm / phiên bản mới để tạo chính sách mới. Quy tắc của bản đã lưu được giữ nguyên.");return;}
        var selected=Selected();
        switch(selected){
            case AreaItem a:await SaveAreaAsync(a);break;
            case CatalogItem c:await SaveNameAsync(c);break;
            case HolidayItem h:
                using(var d=new FieldsDialog("Đổi tên ngày lễ "+h.Date.ToString("dd/MM/yyyy"),new FieldSpec("name","Tên ngày lễ",Initial:h.Name,MaxLength:200))){
                    if(d.ShowDialog(this)!=DialogResult.OK)return;
                    await services!.Catalog.RenameHolidayAsync(new RenameHolidayRequest(h.Date,(string)d.Values["name"]!,h.Name));
                }break;
            case SettingItem s:
                using(var d=new FieldsDialog("Tham số "+s.Key,
                    new FieldSpec("value","Giá trị ("+s.ValueType+", "+s.Minimum+"–"+s.Maximum+" "+s.Format+")",Initial:s.Value))){
                    if(d.ShowDialog(this)!=DialogResult.OK)return;
                    await services!.Catalog.UpdateSettingAsync(new UpdateSettingRequest(s.Key,(string)d.Values["value"]!,s.Value));
                }break;
        }
        await ReloadAsync();
    }
    private async Task SaveAreaAsync(AreaItem? original)
    {
        using var d=new FieldsDialog(original is null?"Thêm khu vực":"Sửa khu vực",new("name","Tên",Initial:original?.Name,MaxLength:150),
            new("province","Tỉnh / thành",Initial:original?.Province,MaxLength:100),
            new("group","Nhóm",FieldKind.Choice,original?.AreaGroup??AreaGroup.InnerCity,Choices:UiNames.AreaGroups()));
        if(d.ShowDialog(this)!=DialogResult.OK)return;
        var r=new AreaRequest{Name=(string)d.Values["name"]!,Province=(string)d.Values["province"]!,AreaGroup=(AreaGroup)d.Values["group"]!};
        if(original is null)await services!.Catalog.CreateAreaAsync(r);else await services!.Catalog.UpdateAreaAsync(original,r);
    }
    private async Task SaveNameAsync(CatalogItem? original)
    {
        var kind=viewTabs.SelectedIndex==1?CatalogKind.Amenity:CatalogKind.RoomCategory;
        using var d=new FieldsDialog(original is null?"Thêm danh mục":"Sửa danh mục",new FieldSpec("name","Tên",Initial:original?.Name,MaxLength:150));
        if(d.ShowDialog(this)!=DialogResult.OK)return;
        var r=new CatalogNameRequest((string)d.Values["name"]!);
        if(original is null)await services!.Catalog.CreateItemAsync(kind,r);else await services!.Catalog.UpdateItemAsync(kind,original,r);
    }
    private async Task ToggleAsync()
    {
        var x=Selected();var active=Active(x);
        if(active is null)return;
        if(MessageBox.Show(this,(active.Value?"Ngừng sử dụng":"Dùng lại")+" dòng đã chọn?","Xác nhận",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        switch(x){
            case AreaItem a:await services!.Catalog.SetActiveAsync(CatalogKind.Area,a.Id,a.IsActive,!a.IsActive);break;
            case CatalogItem c:await services!.Catalog.SetActiveAsync(viewTabs.SelectedIndex==1?CatalogKind.Amenity:CatalogKind.RoomCategory,c.Id,c.IsActive,!c.IsActive);break;
            case CancellationPolicyItem p:await services!.Catalog.SetPolicyActiveAsync(p.Id,p.IsActive,!p.IsActive);break;
        }
        await ReloadAsync();
    }
    private DataTable ExportTable()
    {
        var table=new DataTable();
        for(int i=0;i<CurrentGrid.Grid.Columns.Count;i++)table.Columns.Add("Column"+i,typeof(object));
        foreach(var x in Filtered())table.Rows.Add(Map(x));
        return table;
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(services is not null){
            if(keyData==Keys.F5){reloadButton.PerformClick();return true;}
            if(keyData==(Keys.Control|Keys.F)){filterPanelField0.Focus();return true;}
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
}
