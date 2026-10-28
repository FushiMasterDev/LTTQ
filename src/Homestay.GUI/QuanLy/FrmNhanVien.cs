using System.Data;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Manager;

/// <summary>Màn hình nhân viên: danh sách, tạo tài khoản, khóa/mở khóa, đặt lại mật khẩu.</summary>
public partial class FrmStaff : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader = new();
    private IReadOnlyList<EmployeeListItem> rows = [];
    public FrmStaff()
    {
        InitializeComponent();
        HelpText = "Chọn bộ lọc rồi Tải lại. Bấm đúp để xem hồ sơ. Mật khẩu tạm chỉ hiện một lần; bàn giao riêng cho người nhận. Tab Khu vực lọc nhân viên theo khu vực. Chọn nhân viên trước để xem lịch sử phiên hoặc đổi phạm vi.";
        Disposed += (_,_) => loader.Dispose();
    }
    public FrmStaff(AppServices services) : this()
    {
        if (!services.Can(Permission.EmployeeManage)) throw new ForbiddenException();
        this.services = services;
        pendingNotice.Visible = false;
        foreach(var button in new[]{reloadButton,addButton,lockButton,resetButton}) button.Tag = null;
        roleButton.Visible = resignButton.Visible = endSessionButton.Visible = true;
        roleButton.Tag=resignButton.Tag=endSessionButton.Tag=null;

        filterPanelField3.Visible = filterPanelField3Label.Visible = false;
        filterPanelField1.Items.Clear();
        filterPanelField1.Items.Add(new Choice(null,"Tất cả"));
        filterPanelField1.Items.AddRange(UiNames.Positions()); filterPanelField1.SelectedIndex = 0;
        filterPanelField2.Items.Clear(); filterPanelField2.Items.Add(new Choice(null,"Tất cả"));
        filterPanelField2.Items.AddRange(Enum.GetValues<UserStatus>().Select(s=>new Choice(s,UiNames.UserStatus(s))).ToArray());
        filterPanelField2.SelectedIndex = 0;
        grid0.SetColumns("Mã","Họ tên","Điện thoại","Chức vụ","Làm việc","Tài khoản","Khu vực","Đăng nhập gần nhất","Đang đăng nhập");
        grid0.SetSortKeys("EmployeeCode","FullName","Phone","Position","EmploymentStatus","AccountStatus",null,"LastLoginAtUtc",null);
        grid0.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F18.staff");
        grid0.QueryRequested += async (_,e)=>await LoadAsync(e.Request);
        grid0.DetailRequested += async (_,_)=>await ErrorHandler.RunAsync(reloadButton,DetailAsync);
        grid0.ExportFilteredAsync = ExportAsync;
        reloadButton.Click += async (_,_)=>{if(viewTabs.SelectedTab==tab0)await LoadAsync(grid0.Query with{PageNumber=1});else await ReloadManagerTabAsync();};
        addButton.Click += async (_,_)=>await RunMutationAsync(addButton,CreateAsync,ReloadAsync);
        lockButton.Click += async (_,_)=>await RunMutationAsync(lockButton,ToggleLockAsync,ReloadAsync);
        resetButton.Click += async (_,_)=>await RunMutationAsync(resetButton,ResetAsync,ReloadAsync);
        filterPanelField0.KeyDown += (_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;reloadButton.PerformClick();}};
        InitializeManagerFeatures();
        Shown += async (_,_)=>await ReloadAsync();
    }
    private EmployeeSearchRequest Search(PageRequest page) => new() {
        Page=page with{SearchText=filterPanelField0.Text.Trim()},
        Position=(filterPanelField1.SelectedItem as Choice)?.Value as Position?,
        AccountStatus=(filterPanelField2.SelectedItem as Choice)?.Value as UserStatus? };
    private Task ReloadAsync()=>LoadAsync(grid0.Query);
    private async Task LoadAsync(PageRequest page)
    {
        if(services is null)return;
        rows=[];targetEmployee=null;RefreshManagerButtons();roleButton.Enabled=resignButton.Enabled=areasButton.Enabled=lockButton.Enabled=resetButton.Enabled=false;var request=Search(page);
        try {
            await loader.LoadAsync(ct=>services.Staff.ListAsync(request,ct),result=>{
                rows=result.Items;
                grid0.SetPage(result,Map);lockButton.Enabled=resetButton.Enabled=result.Items.Count>0;
            });
        } catch(Exception exception){if(!IsDisposed)ErrorHandler.Show(exception,this);}
    }
    private static object?[] Map(EmployeeListItem x)=>[
        x.EmployeeCode,x.FullName,x.Phone,UiNames.Position(x.Position),UiNames.Employment(x.EmploymentStatus),
        UiNames.UserStatus(x.AccountStatus),x.HasAllAreas?"Toàn hệ thống":x.AreaNames,
        x.LastLoginAtUtc is DateTime date?UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(date,DateTimeKind.Utc))).ToString("dd/MM/yyyy HH:mm"):"",
        x.HasOpenSession?"Có":"Không"];
    private EmployeeListItem Selected()
    {
        if(grid0.Grid.SelectedRows.Count!=1)throw new InvalidDataException("Vui lòng chọn một nhân viên.");
        return rows[grid0.Grid.SelectedRows[0].Index];
    }
    private async Task DetailAsync()
    {
        var detail=await services!.Staff.GetAsync(Selected().EmployeeId);
        var x=detail.Employee;
        MessageBox.Show(this,$"{x.EmployeeCode} · {x.FullName}\nĐiện thoại: {x.Phone}\nEmail: {detail.Email}\nChức vụ: {UiNames.Position(x.Position)}\nTài khoản: {UiNames.UserStatus(x.AccountStatus)}\nLý do khóa: {detail.LockReason}\nKhu vực: {(x.HasAllAreas?"Toàn hệ thống":x.AreaNames)}\nCần đổi mật khẩu: {(detail.MustChangePassword?"Có":"Không")}","Hồ sơ nhân viên");
    }
    private async Task CreateAsync()
    {
        using var dialog=new FieldsDialog("Tạo nhân viên",
            new("name","Họ tên",MaxLength:150),new("phone","Điện thoại",MaxLength:20),
            new("email","Email",Required:false,MaxLength:254),
            new("position","Chức vụ",FieldKind.Choice,Position.Staff,Choices:UiNames.Positions()));
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var v=dialog.Values;
        var result=await services!.Staff.CreateAsync(new CreateEmployeeRequest{
            FullName=(string)v["name"]!,Phone=(string)v["phone"]!,Email=(string?)v["email"],
            Position=(Position)v["position"]!});
        using(var secret=new SecretDialog(result.EmployeeCode,result.TemporaryPassword))secret.ShowDialog(this);
        await ReloadAsync();
    }
    private async Task ToggleLockAsync()
    {
        var x=Selected();
        var request=new AccountActionRequest{UserId=x.UserId,RowVer=x.RowVer.ToArray()};
        if(x.AccountStatus==UserStatus.Locked){
            if(MessageBox.Show(this,"Mở khóa tài khoản "+x.EmployeeCode+"?","Xác nhận",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            await services!.Staff.UnlockAccountAsync(request);
        } else {
            using var dialog=new FieldsDialog("Khóa tài khoản "+x.EmployeeCode,new FieldSpec("reason","Lý do khóa",FieldKind.Multiline));
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            await services!.Staff.LockAccountAsync(request with{Reason=(string)dialog.Values["reason"]!});
        }
        await ReloadAsync();
    }
    private async Task ResetAsync()
    {
        var x=Selected();
        if(MessageBox.Show(this,"Đặt lại mật khẩu cho "+x.EmployeeCode+"? Người này phải đổi mật khẩu tạm khi đăng nhập.","Xác nhận",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        var result=await services!.Staff.ResetPasswordAsync(new AccountActionRequest{UserId=x.UserId,RowVer=x.RowVer.ToArray()});
        using(var secret=new SecretDialog(x.EmployeeCode,result.TemporaryPassword))secret.ShowDialog(this);
        await ReloadAsync();
    }
    private async Task<DataTable> ExportAsync(CancellationToken ct)
    {
        var request=Search(grid0.Query with{PageNumber=1,PageSize=100});
        var table=new DataTable();
        for(int i=0;i<grid0.Grid.Columns.Count;i++)table.Columns.Add("Column"+i,typeof(object));
        int count;
        do {
            var result=await services!.Staff.ListAsync(request,ct);
            foreach(var x in result.Items)table.Rows.Add(Map(x));
            count=result.TotalCount;
            if(result.Items.Count==0)break;
            request=request with{Page=request.Page with{PageNumber=request.Page.PageNumber+1}};
        } while(table.Rows.Count<count);
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
