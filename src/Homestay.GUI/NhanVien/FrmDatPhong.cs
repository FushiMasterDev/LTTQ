using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmBookings : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new();
    private readonly AsyncLoader actionLoader=new();
    private IReadOnlyList<BookingAction> selectionActions=[];
    private readonly Button clearFilters=new(){Text="Xóa lọc",AutoSize=true};
    private readonly Label filterSummary=new(){AutoSize=true,Padding=new Padding(4,8,4,4)};
    private readonly System.Diagnostics.Stopwatch serverWatch=new();
    private DateTime serverAnchor;
    private System.Windows.Forms.Timer? deadlineTimer;
    private IReadOnlyList<BookingListItem> rows=[];
    private BookingSearchRequest basis=new();
    private bool initializingFilters=true;
    private readonly BookingQueue[] queues=[BookingQueue.Mine,BookingQueue.Unassigned,BookingQueue.AwaitingPayment,BookingQueue.DepositVerification,BookingQueue.OwnerConfirmation,BookingQueue.ArrivalsToday,BookingQueue.DeparturesToday,BookingQueue.All,BookingQueue.Upcoming,BookingQueue.InHouse,BookingQueue.OpenChangeRequests];
    private readonly PagedGrid upcomingGrid=new(),inHouseGrid=new(),changesGrid=new();
    private PagedGrid[] Grids=>[grid0,grid1,grid2,grid3,grid4,grid5,grid6,grid7,upcomingGrid,inHouseGrid,changesGrid];
    private PagedGrid Current=>IsExceptions?exceptionsGrid:Grids[viewTabs.SelectedIndex];
    public FrmBookings(){InitializeComponent();HelpText="Tìm mã đơn hoặc SĐT đầy đủ để tra toàn hệ thống. Chi tiết ngoài phạm vi chỉ được xem. BLL kiểm lại quyền và phiên khi ghi.";Disposed+=(_,_)=>{loader.Dispose();actionLoader.Dispose();upcomingGrid.Dispose();inHouseGrid.Dispose();changesGrid.Dispose();exceptionsGrid.Dispose();exceptionsTab.Dispose();};}
    public FrmBookings(AppServices services,BookingSearchRequest? request=null,BookingExceptionsSelection? exceptions=null):this(){
        this.services=services;basis=request??new();
        filterPanelField0.Text=basis.Code??basis.Phone??basis.Page.SearchText??basis.CustomerName??"";
        basis=basis with{Code=null,Phone=null,CustomerName=null};
        toolbar.Controls.AddRange([clearFilters,filterSummary]);
        EmployeeUi.Activate(pendingNotice,reloadButton,detailButton,claimButton,assignButton);tab7.Text="Tất cả";
        foreach(var item in new[]{("Sắp đến",upcomingGrid),("Đang ở",inHouseGrid),("Yêu cầu đang mở",changesGrid)}){
            var tab=new TabPage(item.Item1){Padding=new Padding(8)};item.Item2.Dock=DockStyle.Fill;tab.Controls.Add(item.Item2);viewTabs.TabPages.Add(tab);
        }
        viewTabs.Multiline=true;
        filterPanelField1Label.Text="Khu vực";filterPanelField2Label.Text="Homestay";filterPanelField6Label.Text="Người phụ trách";
        filterPanelField3.ShowCheckBox=filterPanelField4.ShowCheckBox=true;filterPanelField3.Checked=filterPanelField4.Checked=false;
        filterPanelField5.Items.Clear();filterPanelField5.DisplayMember=nameof(Choice.Label);filterPanelField5.Items.Add(new Choice(null,"Tất cả"));
        filterPanelField5.Items.AddRange(Enum.GetValues<BookingStatus>().Select(s=>new Choice(s,EmployeeUi.Status(s))).ToArray());filterPanelField5.SelectedIndex=0;
        viewTabs.SelectedIndex=Array.IndexOf(queues,basis.Queue) is var i&&i>=0?i:7;
        foreach(var g in Grids){
            g.Grid.SelectionChanged+=async(_,_)=>{if(g==Current){assignManyButton.Enabled=CanAssignMany();await RefreshSelectionActionsAsync();}};
            g.Grid.CellFormatting+=(_,e)=>{
                if(g!=Current||e.ColumnIndex!=10||e.RowIndex<0||e.RowIndex>=rows.Count||serverAnchor==default)return;
                var item=rows[e.RowIndex];
                e.Value=DeadlineDisplay.Format(item.DeadlineAtUtc,serverAnchor+serverWatch.Elapsed);e.FormattingApplied=true;
                if(item.IsConfirmationOverdue||item.Status==BookingStatus.Deposited&&item.DeadlineAtUtc<serverAnchor+serverWatch.Elapsed)e.CellStyle!.BackColor=Color.MistyRose;
            };
            EmployeeUi.BookingColumns(g);g.Grid.Columns[10].DefaultCellStyle.WrapMode=DataGridViewTriState.True;g.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F08."+Array.IndexOf(Grids,g));g.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);g.DetailRequested+=(_,_)=>detailButton.PerformClick();
            g.ExportFilteredAsync=async ct=>{var all=new List<BookingListItem>();var q=Search(g.Query with{PageNumber=1,PageSize=100});
                while(true){var p=await services.Bookings.ListAsync(q,ct);all.AddRange(p.Items);if(p.Items.Count==0||all.Count>=p.TotalCount)break;q=q with{Page=q.Page with{PageNumber=q.Page.PageNumber+1}};}
                return EmployeeUi.Table(g,all.Select(EmployeeUi.BookingRow));};
        }
        reloadButton.Click+=async(_,_)=>await LoadAsync(Current.Query with{PageNumber=1});
        filterPanelField1.SelectionChanged+=async(_,_)=>{if(!initializingFilters)await ReloadLookupsAndBookingsAsync(filterPanelField1.SelectedItem?.Id,null,null);};
        filterPanelField2.SelectionChanged+=async(_,_)=>{if(!initializingFilters)await ReloadLookupsAndBookingsAsync(filterPanelField1.SelectedItem?.Id,filterPanelField2.SelectedItem?.Id,null);};
        filterPanelField6.SelectionChanged+=async(_,_)=>{if(!initializingFilters)await LoadAsync(Current.Query with{PageNumber=1});};
        viewTabs.SelectedIndexChanged+=async(_,_)=>{if(initializingFilters)return;basis=basis with{OnlyExpiringHolds=false,OnlyOverdueConfirmation=false,AssignedToMeOnly=false};await LoadAsync(Current.Query with{PageNumber=1});};
        detailButton.Click+=(_,_)=>{try{services.Navigate("F09",IsExceptions?exceptionsLive!.Selected.BookingId:Selected().Id);}catch(Exception e){ErrorHandler.Show(e,this);}};
        claimButton.Enabled=assignButton.Enabled=false;assignButton.Visible=services.Can(Permission.BookingAssign);
        clearFilters.Click+=async(_,_)=>{
            basis=new(){Queue=IsExceptions?BookingQueue.All:queues[viewTabs.SelectedIndex]};filterPanelField0.Clear();
            filterPanelField3.Checked=filterPanelField4.Checked=false;filterPanelField5.SelectedIndex=0;
            await ReloadLookupsAndBookingsAsync(null,null,null);
        };
        deadlineTimer=new(components!){Interval=1000};
        deadlineTimer.Tick+=(_,_)=>{if(!IsDisposed&&!IsExceptions&&Current.Grid.Columns.Count>10)Current.Grid.InvalidateColumn(10);};
        deadlineTimer.Start();
        claimButton.Click+=async(_,_)=>await RunMutationAsync(claimButton,async()=>{var x=Selected();await services.Bookings.ClaimAsync(new(x.Id,x.RowVer));await ReloadAsync();},ReloadAsync,()=>selectionActions.Contains(BookingAction.Claim));
        assignButton.Click+=async(_,_)=>await RunMutationAsync(assignButton,async()=>{
            await EmployeeUi.ReassignAsync(services,Selected(),this);await ReloadAsync();
        },ReloadAsync,()=>selectionActions.Contains(BookingAction.Reassign)&&services.Can(Permission.BookingAssign));
        InitializeManagerBookings(exceptions);initializingFilters=false;Shown+=async(_,_)=>await ReloadLookupsAndBookingsAsync(basis.AreaId,basis.HomestayId,basis.AssignedEmployeeId);
    }
    private async Task RefreshSelectionActionsAsync(){
        actionLoader.CancelPending();selectionActions=[];claimButton.Enabled=assignButton.Enabled=false;
        if(IsExceptions){RefreshExceptionActions();return;}if(services is null||Current.Grid.SelectedRows.Count!=1)return;
        var index=Current.Grid.SelectedRows[0].Index;if(index>=rows.Count)return;
        var id=rows[index].Id;
        try{await actionLoader.LoadAsync(ct=>services.Bookings.GetAsync(id,ct),detail=>{
            selectionActions=detail.AllowedActions;
            claimButton.Enabled=selectionActions.Contains(BookingAction.Claim);
            assignButton.Enabled=services.Can(Permission.BookingAssign)&&selectionActions.Contains(BookingAction.Reassign);
        });}catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private BookingListItem Selected()=>rows[EmployeeUi.Selected(Current)];
    private BookingSearchRequest Search(PageRequest q)=>basis with{Page=q with{SearchText=filterPanelField0.Text},Queue=queues[viewTabs.SelectedIndex],
        AreaId=filterPanelField1.SelectedItem?.Id,HomestayId=filterPanelField2.SelectedItem?.Id,AssignedEmployeeId=filterPanelField6.SelectedItem?.Id,
        Status=(filterPanelField5.SelectedItem as Choice)?.Value as BookingStatus?,CustomerName=null,
        CheckInFrom=filterPanelField3.Checked?DateOnly.FromDateTime(filterPanelField3.Value):null,CheckInTo=filterPanelField4.Checked?DateOnly.FromDateTime(filterPanelField4.Value):null};
    private Task ReloadAsync()=>LoadAsync(Current.Query);
    private async Task ReloadLookupsAndBookingsAsync(int? areaId,int? homestayId,int? assigneeId){
        initializingFilters=true;
        try{
            var areas=await services!.Bookings.ListFilterAreasAsync();filterPanelField1.SetItems(areas);filterPanelField1.SelectId(areaId);
            var homes=await services.Bookings.ListFilterHomestaysAsync(areaId);filterPanelField2.SetItems(homes);filterPanelField2.SelectId(homestayId);
            var assignees=await services.Bookings.ListFilterAssigneesAsync(areaId,homestayId);filterPanelField6.SetItems(assignees);filterPanelField6.SelectId(assigneeId);
        }catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
        finally{initializingFilters=false;}
        await LoadAsync(Current.Query with{PageNumber=1});
    }
    private async Task LoadAsync(PageRequest q){
        var g=Current;assignManyButton.Enabled=createTaskButton.Enabled=detailButton.Enabled=false;assignManyButton.Visible=IsExceptions&&services!.Can(Permission.BookingAssign);exceptionKind.Visible=IsExceptions;createTaskButton.Visible=IsExceptions;filterPanelField3.Enabled=filterPanelField4.Enabled=filterPanelField5.Enabled=filterPanelField6.Enabled=!IsExceptions;if(IsExceptions){loader.CancelPending();actionLoader.CancelPending();claimButton.Enabled=assignButton.Enabled=false;await exceptionsLive!.LoadAsync(q);return;}detailButton.Enabled=true;var request=Search(q);actionLoader.CancelPending();selectionActions=[];claimButton.Enabled=assignButton.Enabled=false;
        filterSummary.Text=request.OnlyExpiringHolds?"Giữ phòng sắp hết hạn":request.OnlyOverdueConfirmation?"Chờ chủ quá hạn":"";
        rows=[];g.SetPage(new Homestay.DTO.Common.PagedResult<BookingListItem>{Items=[],TotalCount=0,PageNumber=1,PageSize=q.PageSize},EmployeeUi.BookingRow);
        try{await loader.LoadAsync(async ct=>{var result=await services!.Bookings.ListAsync(request,ct);var now=await services.ServerNowAsync(ct);return (Result:result,Now:now);},loaded=>{var p=loaded.Result;serverAnchor=loaded.Now;serverWatch.Restart();rows=p.Items;g.SetPage(p,EmployeeUi.BookingRow);assignManyButton.Enabled=CanAssignMany();foreach(DataGridViewRow row in g.Grid.Rows){var x=p.Items[row.Index];if(x.IsConfirmationOverdue)row.DefaultCellStyle.BackColor=Color.MistyRose;}});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys k){if(services is not null&&(k==Keys.F5||k==Keys.Enter&&filterPanelField0.Focused)){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);}
}
