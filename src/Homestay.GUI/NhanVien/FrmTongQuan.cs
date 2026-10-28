using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.DTO.Bookings;

namespace Homestay.GUI.Staff;
/// <summary>Màn hình tổng quan: số đơn theo hàng đợi và các việc gấp cần xử lý.</summary>
public partial class FrmDashboard : ScreenForm
{
    private AppServices? services;private readonly Button waitingReceipt=new(){AutoSize=true},waitingCancel=new(){AutoSize=true},lateTasks=new(){AutoSize=true},unassignedExceptions=new(){AutoSize=true};
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<DashboardUrgentItem> urgent=[];
    public FrmDashboard() { InitializeComponent(); HelpText="Số liệu theo phạm vi phụ trách. Bấm thẻ để mở danh sách tương ứng."; Disposed+=(_,_)=>loader.Dispose(); }
    public FrmDashboard(AppServices services):this() {
        this.services=services;EmployeeUi.Activate(pendingNotice,reloadButton);
        filterViewport.Visible=false;body.ColumnStyles[0].Width=0;
        foreach(var card in new[]{cards0Item0,cards0Item1,cards0Item2,cards0Item3,cards0Item4,cards0Item5})card.Tag=null;
        cards1.Visible=cards2.Visible=false;viewTabs.TabPages.Remove(tab2);viewTabs.TabPages.Remove(tab3);
        reloadButton.Click+=async(_,_)=>await ReloadAsync();
        if(services.Session?.Position==Homestay.DTO.Enums.Position.Manager){toolbar.Controls.AddRange([waitingReceipt,waitingCancel,lateTasks,unassignedExceptions]);waitingReceipt.Click+=(_,_)=>services.Navigate("F08",new BookingSearchRequest{Queue=BookingQueue.DepositVerification});waitingCancel.Click+=(_,_)=>services.Navigate("F20",null);lateTasks.Click+=(_,_)=>services.Navigate("F19",new TaskBoardSelection(OnlyOverdue:true));unassignedExceptions.Click+=(_,_)=>services.Navigate("F08",new BookingExceptionsSelection(BookingExceptionKind.Unassigned));}grid0.SetColumns("Công việc","Mã đơn","Khách","Hạn xử lý");
        grid1.SetColumns("Hàng đợi","Số đơn","Ngày nghiệp vụ");
        grid0.DetailRequested+=(_,_)=>{try{var row=urgent[(grid0.Query.PageNumber-1)*grid0.Query.PageSize+EmployeeUi.Selected(grid0)];if(row.BookingId is int id)services.Navigate("F09",id);}catch(Exception e){ErrorHandler.Show(e,this);}};
        Shown+=async(_,_)=>await ReloadAsync();
    }
    private void Card(Button b,string text,int count,object? request) {
        b.Tag=null;b.Text=text+"\n"+count;b.Enabled=request is not null;
        b.Tag=request;
    }
    private async Task ReloadAsync() {
        try { if(services!.Session?.Position==Homestay.DTO.Enums.Position.Accountant){await loader.LoadAsync(ct=>services.Accounting.GetAccountantDashboardAsync(ct),d=>{
            var cards=new[]{cards0Item0,cards0Item1,cards0Item2,cards0Item3,cards0Item4,cards0Item5};var labels=new[]{("Chờ khớp",d.UnmatchedTransactions.ToString("N0",UiFormat.Vietnamese)),("Chuyển thiếu",d.UnderpaidTransactions.ToString("N0",UiFormat.Vietnamese)),("Phiếu thu chờ",d.PendingDeposits.ToString("N0",UiFormat.Vietnamese)),("Phiếu hoàn chờ",d.PendingRefunds.ToString("N0",UiFormat.Vietnamese)),("Đối soát chờ trả",d.SentSettlements.ToString("N0",UiFormat.Vietnamese)),("Công nợ",UiFormat.Money(d.OutstandingPayables))};
            var targets=new[]{"F28","F28","F27","F29","F30","F32"};for(var i=0;i<cards.Length;i++){cards[i].Text=labels[i].Item1+"\n"+labels[i].Item2;cards[i].Tag=targets[i];cards[i].Enabled=true;}
            var t=new System.Data.DataTable();for(var i=0;i<4;i++)t.Columns.Add("Column"+i,typeof(object));foreach(var x in new[]{("Chờ khớp giao dịch",d.UnmatchedTransactions),("Chuyển thiếu tiền",d.UnderpaidTransactions),("Phiếu thu cần xác minh",d.PendingDeposits),("Hoàn tiền cần chi",d.PendingRefunds),("Đối soát chờ chuyển chủ",d.SentSettlements)})t.Rows.Add(x.Item1,x.Item2,"Tài chính","");grid0.SetPage(t,t.Rows.Count,1);var summary=new System.Data.DataTable();for(var i=0;i<3;i++)summary.Columns.Add("Column"+i,typeof(object));summary.Rows.Add("Công nợ hiện tại",UiFormat.Money(d.OutstandingPayables),"Sổ công nợ");grid1.SetPage(summary,1,1);
        });return;} if(services.Session?.Position==Homestay.DTO.Enums.Position.Manager){await loader.LoadAsync(async ct=>{
            var manager=await services.Bookings.GetManagerDashboardAsync(ct);var exceptions=await services.Bookings.ListExceptionsAsync(new(){Page=new(){PageSize=100}},ct);return(manager,exceptions);
        },result=>{var d=result.manager;
            Card(cards0Item0,"Đơn chưa giao",d.Unassigned,new BookingSearchRequest(){Queue=BookingQueue.Unassigned});
            Card(cards0Item1,"Giữ phòng sắp hết",d.Exceptions.FirstOrDefault(x=>x.Kind==BookingExceptionKind.HoldExpiring)?.Count??0,new BookingExceptionsSelection(BookingExceptionKind.HoldExpiring));
            Card(cards0Item2,"Chờ chủ quá hạn",d.Exceptions.FirstOrDefault(x=>x.Kind==BookingExceptionKind.ConfirmationOverdue)?.Count??0,new BookingExceptionsSelection(BookingExceptionKind.ConfirmationOverdue));
            Card(cards0Item3,"Khách đến hôm nay",d.ArrivalsToday,new BookingSearchRequest(){Queue=BookingQueue.ArrivalsToday});
            Card(cards0Item4,"Khách đi hôm nay",d.DeparturesToday,new BookingSearchRequest(){Queue=BookingQueue.DeparturesToday});
            Card(cards0Item5,"Phiếu thu chờ lâu",d.Exceptions.FirstOrDefault(x=>x.Kind==BookingExceptionKind.ReceiptWaiting)?.Count??0,new BookingExceptionsSelection(BookingExceptionKind.ReceiptWaiting));waitingReceipt.Text="Phiếu thu chờ: "+d.PendingReceipts;waitingCancel.Text="Hủy chờ duyệt: "+d.PendingCancellations;lateTasks.Text="Việc quá hạn: "+d.OverdueTasks;unassignedExceptions.Text="Ngoại lệ chưa giao: "+(d.Exceptions.FirstOrDefault(x=>x.Kind==BookingExceptionKind.Unassigned)?.Count??0);
            urgent=result.exceptions.Items.Select(x=>new DashboardUrgentItem(x.BookingId,x.BookingCode,"",x.Problem,x.DeadlineAtUtc??x.SinceUtc,null)).ToArray();
            EmployeeUi.Local(grid0,urgent,x=>[x.Work,x.BookingCode,x.CustomerName,EmployeeUi.Time(x.DeadlineAtUtc)]);
            EmployeeUi.Local(grid1,new object?[][]{["Yêu cầu hủy chờ duyệt",d.PendingCancellations,EmployeeUi.Date(d.Today)],["Việc giao quá hạn",d.OverdueTasks,EmployeeUi.Date(d.Today)]},x=>x);
        });return;}
        await loader.LoadAsync(ct=>services.Bookings.GetDashboardAsync(ct),d=>{
            Card(cards0Item0,"Đơn của tôi",d.MyQueues.FirstOrDefault(q=>q.Queue==BookingQueue.Mine)?.Count??0,new BookingSearchRequest(){Queue=BookingQueue.Mine});
            Card(cards0Item1,"Giữ phòng sắp hết",d.ExpiringHolds,new BookingSearchRequest(){Queue=BookingQueue.All,OnlyExpiringHolds=true,AssignedToMeOnly=true});
            Card(cards0Item2,"Chờ chủ quá hạn",d.OverdueConfirmations,new BookingSearchRequest(){Queue=BookingQueue.All,OnlyOverdueConfirmation=true,AssignedToMeOnly=true});
            Card(cards0Item3,"Khách đến hôm nay",d.ArrivalsToday,new BookingSearchRequest(){Queue=BookingQueue.ArrivalsToday});
            Card(cards0Item4,"Khách đi hôm nay",d.DeparturesToday,new BookingSearchRequest(){Queue=BookingQueue.DeparturesToday});
            Card(cards0Item5,"Đơn chưa giao",d.Unassigned,new BookingSearchRequest(){Queue=BookingQueue.Unassigned});
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
            b.Click+=(_,_)=>{if(services.Session?.Position==Homestay.DTO.Enums.Position.Accountant&&b.Tag is string screen)services.Navigate(screen,null);else if(b.Tag is BookingExceptionsSelection ex)services.Navigate("F08",ex);else if(b.Tag is BookingSearchRequest r)services.Navigate("F08",r);};
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keys) {
        if(services is not null&&keys==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,keys);
    }
}
