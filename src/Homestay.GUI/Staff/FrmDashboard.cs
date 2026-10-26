using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.DTO.Bookings;

namespace Homestay.GUI.Staff;
public partial class FrmDashboard : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<DashboardUrgentItem> urgent=[];
    public FrmDashboard() { InitializeComponent(); HelpText="Số liệu theo phạm vi phụ trách. Bấm thẻ để mở danh sách tương ứng."; Disposed+=(_,_)=>loader.Dispose(); }
    public FrmDashboard(AppServices services):this() {
        this.services=services;EmployeeUi.Activate(pendingNotice,reloadButton);
        filterViewport.Visible=false;body.ColumnStyles[0].Width=0;
        foreach(var card in new[]{cards0Item0,cards0Item1,cards0Item2,cards0Item3,cards0Item4,cards0Item5})card.Tag=null;
        cards1.Visible=cards2.Visible=false;viewTabs.TabPages.Remove(tab2);viewTabs.TabPages.Remove(tab3);
        reloadButton.Click+=async(_,_)=>await ReloadAsync();
        grid0.SetColumns("Công việc","Mã đơn","Khách","Hạn xử lý");
        grid1.SetColumns("Hàng đợi","Số đơn","Ngày nghiệp vụ");
        grid0.DetailRequested+=(_,_)=>{try{var row=urgent[(grid0.Query.PageNumber-1)*grid0.Query.PageSize+EmployeeUi.Selected(grid0)];if(row.BookingId is int id)services.Navigate("F09",id);}catch(Exception e){ErrorHandler.Show(e,this);}};
        Shown+=async(_,_)=>await ReloadAsync();
    }
    private void Card(Button b,string text,int count,BookingSearchRequest? request) {
        b.Tag=null;b.Text=text+"\n"+count;b.Enabled=request is not null;
        b.Tag=request;
    }
    private async Task ReloadAsync() {
        try { await loader.LoadAsync(ct=>services!.Bookings.GetDashboardAsync(ct),d=>{
            Card(cards0Item0,"Đơn của tôi",d.MyQueues.FirstOrDefault(q=>q.Queue==BookingQueue.Mine)?.Count??0,new(){Queue=BookingQueue.Mine});
            Card(cards0Item1,"Giữ phòng sắp hết",d.ExpiringHolds,new(){Queue=BookingQueue.All,OnlyExpiringHolds=true,AssignedToMeOnly=true});
            Card(cards0Item2,"Chờ chủ quá hạn",d.OverdueConfirmations,new(){Queue=BookingQueue.All,OnlyOverdueConfirmation=true,AssignedToMeOnly=true});
            Card(cards0Item3,"Khách đến hôm nay",d.ArrivalsToday,new(){Queue=BookingQueue.ArrivalsToday});
            Card(cards0Item4,"Khách đi hôm nay",d.DeparturesToday,new(){Queue=BookingQueue.DeparturesToday});
            Card(cards0Item5,"Đơn chưa giao",d.Unassigned,new(){Queue=BookingQueue.Unassigned});
            urgent=d.UrgentItems;EmployeeUi.Local(grid0,d.UrgentItems,x=>[x.Work,x.BookingCode,x.CustomerName,EmployeeUi.Time(x.DeadlineAtUtc)]);
            var summary=d.MyQueues.Select(x=>new object?[]{EmployeeUi.Queue(x.Queue),x.Count,EmployeeUi.Date(d.Today)})
                .Concat(new[]{new object?[]{"Việc đến hạn",d.DueTasks,EmployeeUi.Date(d.Today)},new object?[]{"Thông báo chưa đọc",d.UnreadNotifications,EmployeeUi.Date(d.Today)}}).ToArray();
            EmployeeUi.Local(grid1,summary,x=>x);
        }); }catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    protected override void OnLoad(EventArgs e) {
        base.OnLoad(e);
        if(services is null)return;
        foreach(var b in new[]{cards0Item0,cards0Item1,cards0Item2,cards0Item3,cards0Item4,cards0Item5})
            b.Click+=(_,_)=>{if(b.Tag is BookingSearchRequest r)services.Navigate("F08",r);};
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys) {
        if(services is not null&&keys==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,keys);
    }
}