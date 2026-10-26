using System.Data;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Customers;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmCustomers : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<CustomerItem> rows=[];
    private IReadOnlyList<CustomerBookingItem> history=[];
    private readonly Button phoneButton=new(){Text="Đổi SĐT",AutoSize=true};
    public FrmCustomers(){InitializeComponent();HelpText="Tìm tên, email hoặc SĐT. Bấm đúp khách để xem lịch sử. CCCD mới để trống sẽ giữ nguyên giá trị cũ.";Disposed+=(_,_)=>loader.Dispose();}
    public FrmCustomers(AppServices services,CustomerItem? initial=null):this(){
        this.services=services;if(initial is not null)filterPanelField0.Text=initial.Phone;EmployeeUi.Activate(pendingNotice,reloadButton,addButton,editButton,bookButton);
        addButton.Enabled=editButton.Enabled=services.Session!.Position!=Position.Accountant;
        bookButton.Enabled=services.Can(Permission.BookingCreate);
        toolbar.Controls.Add(phoneButton);phoneButton.Enabled=services.Can(Permission.CustomerPhoneChange);
        grid0.SetColumns("Mã","Họ tên","SĐT","Email","CCCD / hộ chiếu","Ghi chú");
        grid0.SetSortKeys("Id","FullName","Phone","Email",null,null);grid1.SetColumns("Mã đơn","Homestay","Ngày nhận","Ngày trả","Trạng thái","Thanh toán");
        grid0.SetPreferenceIdentity(services.Session.UserId.ToString(),"F13");
        grid0.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);
        reloadButton.Click+=async(_,_)=>await LoadAsync(grid0.Query with{PageNumber=1});
        grid0.DetailRequested+=async(_,_)=>await ErrorHandler.RunAsync(reloadButton,HistoryAsync);
        grid1.DetailRequested+=(_,_)=>{try{services.Navigate("F09",history[(grid1.Query.PageNumber-1)*grid1.Query.PageSize+EmployeeUi.Selected(grid1)].Id);}catch(Exception e){ErrorHandler.Show(e,this);}};
        addButton.Click+=async(_,_)=>await RunMutationAsync(addButton,()=>EditAsync(true),ReloadAsync);
        editButton.Click+=async(_,_)=>await RunMutationAsync(editButton,()=>EditAsync(false),ReloadAsync);
        phoneButton.Click+=async(_,_)=>await RunMutationAsync(phoneButton,async()=>{
            var x=Selected();using var d=new FieldsDialog("Đổi SĐT",new FieldSpec("phone","SĐT mới",Initial:x.Phone,MaxLength:20));
            if(d.ShowDialog(this)==DialogResult.OK)await services.Customers.ChangePhoneAsync(new(x.Id,(string)d.Values["phone"]!,x.RowVer));
        },ReloadAsync);
        bookButton.Click+=(_,_)=>{try{services.Navigate("F07",Selected());}catch(Exception e){ErrorHandler.Show(e,this);}};
        grid0.ExportFilteredAsync=async ct=>{
            var all=new List<CustomerItem>();var q=grid0.Query with{PageNumber=1,PageSize=100,SearchText=filterPanelField0.Text};
            while(true){var p=await services.Customers.SearchAsync(new(){Page=q},ct);all.AddRange(p.Items);if(p.Items.Count==0||all.Count>=p.TotalCount)break;q=q with{PageNumber=q.PageNumber+1};}
            return EmployeeUi.Table(grid0,all.Select(Map));
        };
        Shown+=async(_,_)=>await ReloadAsync();
    }
    private CustomerItem Selected()=>rows[EmployeeUi.Selected(grid0)];
    private static object?[] Map(CustomerItem x)=>[x.Id,x.FullName,x.Phone,x.Email,x.IdNumber is null?null:x.IsIdentityMasked?x.IdNumber:"••••••••"+x.IdNumber[^Math.Min(4,x.IdNumber.Length)..],x.Note];
    private Task ReloadAsync()=>LoadAsync(grid0.Query);
    private async Task LoadAsync(PageRequest p){
        try{await loader.LoadAsync(ct=>services!.Customers.SearchAsync(new(){Page=p with{SearchText=filterPanelField0.Text}},ct),r=>{rows=r.Items;grid0.SetPage(r,Map);});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private async Task HistoryAsync(){
        var detail=await services!.Customers.GetAsync(Selected().Id);history=detail.RecentBookings;
        EmployeeUi.Local(grid1,history,x=>[x.Code,x.HomestayName,EmployeeUi.Date(x.CheckIn),EmployeeUi.Date(x.CheckOut),EmployeeUi.Status(x.Status),x.PaymentStateName]);
        viewTabs.SelectedTab=tab1;
    }
    private async Task EditAsync(bool create){
        var x=create?null:(await services!.Customers.GetAsync(Selected().Id)).Customer;
        using var d=new FieldsDialog(create?"Tạo khách hàng":"Sửa khách hàng",
            new("name","Họ tên",Initial:x?.FullName,MaxLength:100),
            new("phone","SĐT (khi tạo mới)",Initial:create?"":x!.Phone,MaxLength:20),
            new("email","Email",Initial:x?.Email,Required:false,MaxLength:256),
            new("id","CCCD mới (trống: giữ nguyên)",Required:false,MaxLength:20),
            new("clear","Xóa CCCD",FieldKind.Choice,false,Choices:[new(false,"Giữ / thay bằng số mới"),new(true,"Xóa số đã lưu")]),
            new("note","Ghi chú",FieldKind.Multiline,x?.Note,Required:false,MaxLength:1000));
        if(d.ShowDialog(this)!=DialogResult.OK)return;var v=d.Values;string? id=string.IsNullOrWhiteSpace((string?)v["id"])?null:(string?)v["id"];
        if(create)await services!.Customers.CreateAsync(new(){FullName=(string)v["name"]!,Phone=(string)v["phone"]!,Email=(string?)v["email"],IdNumber=id,Note=(string?)v["note"]});
        else{
            if((string?)v["phone"]!=x!.Phone)throw new BusinessException("Dùng nút Đổi SĐT để thay số điện thoại.");
            await services!.Customers.UpdateAsync(new(){CustomerId=x.Id,FullName=(string)v["name"]!,Email=(string?)v["email"],IdNumber=id,ClearIdNumber=(bool)v["clear"]!,Note=(string?)v["note"],RowVer=x.RowVer});
        }
        await ReloadAsync();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys k){
        if(services is not null&&(k==Keys.F5||k==Keys.Enter&&filterPanelField0.Focused)){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);
    }
}