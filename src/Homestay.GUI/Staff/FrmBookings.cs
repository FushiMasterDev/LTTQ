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
    private IReadOnlyList<BookingListItem> rows=[];
    private BookingSearchRequest basis=new();
    private readonly BookingQueue[] queues=[BookingQueue.Mine,BookingQueue.Unassigned,BookingQueue.AwaitingPayment,BookingQueue.DepositVerification,BookingQueue.OwnerConfirmation,BookingQueue.ArrivalsToday,BookingQueue.DeparturesToday,BookingQueue.All];
    private PagedGrid[] Grids=>[grid0,grid1,grid2,grid3,grid4,grid5,grid6,grid7];
    private PagedGrid Current=>Grids[viewTabs.SelectedIndex];
    public FrmBookings(){InitializeComponent();HelpText="Tìm mã đơn hoặc SĐT đầy đủ để tra toàn hệ thống. Chi tiết ngoài phạm vi chỉ được xem. BLL kiểm lại quyền và phiên khi ghi.";Disposed+=(_,_)=>loader.Dispose();}
    public FrmBookings(AppServices services,BookingSearchRequest? request=null):this(){
        this.services=services;basis=request??new();
        EmployeeUi.Activate(pendingNotice,reloadButton,detailButton,claimButton,assignButton);tab7.Text="Tất cả";
        foreach(var c in new Control[]{filterPanelField1,filterPanelField1Label,filterPanelField2,filterPanelField2Label,filterPanelField6,filterPanelField6Label})c.Visible=false;
        filterPanelField3.ShowCheckBox=filterPanelField4.ShowCheckBox=true;filterPanelField3.Checked=filterPanelField4.Checked=false;
        filterPanelField5.Items.Clear();filterPanelField5.DisplayMember=nameof(Choice.Label);filterPanelField5.Items.Add(new Choice(null,"Tất cả"));
        filterPanelField5.Items.AddRange(Enum.GetValues<BookingStatus>().Select(s=>new Choice(s,EmployeeUi.Status(s))).ToArray());filterPanelField5.SelectedIndex=0;
        viewTabs.SelectedIndex=Array.IndexOf(queues,basis.Queue) is var i&&i>=0?i:7;
        foreach(var g in Grids){EmployeeUi.BookingColumns(g);g.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F08."+Array.IndexOf(Grids,g));g.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);g.DetailRequested+=(_,_)=>detailButton.PerformClick();
            g.ExportFilteredAsync=async ct=>{var all=new List<BookingListItem>();var q=Search(g.Query with{PageNumber=1,PageSize=100});
                while(true){var p=await services.Bookings.ListAsync(q,ct);all.AddRange(p.Items);if(p.Items.Count==0||all.Count>=p.TotalCount)break;q=q with{Page=q.Page with{PageNumber=q.Page.PageNumber+1}};}
                return EmployeeUi.Table(g,all.Select(EmployeeUi.BookingRow));};
        }
        reloadButton.Click+=async(_,_)=>await LoadAsync(Current.Query with{PageNumber=1});
        viewTabs.SelectedIndexChanged+=async(_,_)=>{basis=basis with{OnlyExpiringHolds=false,OnlyOverdueConfirmation=false,AssignedToMeOnly=false};await LoadAsync(Current.Query with{PageNumber=1});};
        detailButton.Click+=(_,_)=>{try{services.Navigate("F09",Selected().Id);}catch(Exception e){ErrorHandler.Show(e,this);}};
        claimButton.Enabled=services.Can(Permission.BookingClaim);assignButton.Enabled=services.Can(Permission.BookingAssign);assignButton.Visible=services.Can(Permission.EmployeeManage);
        claimButton.Click+=async(_,_)=>await RunMutationAsync(claimButton,async()=>{var x=Selected();await services.Bookings.ClaimAsync(new(x.Id,x.RowVer));await ReloadAsync();},ReloadAsync);
        assignButton.Click+=async(_,_)=>await RunMutationAsync(assignButton,async()=>{
            var x=Selected();var staff=new List<Homestay.DTO.Staff.EmployeeListItem>();var page=1;
            while(true){var p=await services.Staff.ListAsync(new(){Page=new(){PageNumber=page,PageSize=100},AccountStatus=UserStatus.Active});staff.AddRange(p.Items);if(staff.Count>=p.TotalCount||p.Items.Count==0)break;page++;}
            var choices=staff.Where(e=>e.Position is Position.Staff or Position.Manager&&e.EmploymentStatus==EmploymentStatus.Active)
                .Select(e=>new Choice(e.EmployeeId,e.EmployeeCode+" · "+e.FullName)).ToArray();
            using var d=new FieldsDialog("Giao lại đơn",new FieldSpec("employee","Người phụ trách",FieldKind.Choice,Choices:choices));
            if(d.ShowDialog(this)==DialogResult.OK)await services.Bookings.ReassignAsync(new(x.Id,(int)d.Values["employee"]!,x.RowVer));await ReloadAsync();
        },ReloadAsync);
        Shown+=async(_,_)=>await ReloadAsync();
    }
    private BookingListItem Selected()=>rows[EmployeeUi.Selected(Current)];
    private BookingSearchRequest Search(PageRequest q)=>basis with{Page=q with{SearchText=filterPanelField0.Text},Queue=queues[viewTabs.SelectedIndex],
        Status=(filterPanelField5.SelectedItem as Choice)?.Value as BookingStatus?,
        CheckInFrom=filterPanelField3.Checked?DateOnly.FromDateTime(filterPanelField3.Value):null,CheckInTo=filterPanelField4.Checked?DateOnly.FromDateTime(filterPanelField4.Value):null};
    private Task ReloadAsync()=>LoadAsync(Current.Query);
    private async Task LoadAsync(PageRequest q){
        var g=Current;var request=Search(q);rows=[];g.SetPage(new Homestay.DTO.Common.PagedResult<BookingListItem>{Items=[],TotalCount=0,PageNumber=1,PageSize=q.PageSize},EmployeeUi.BookingRow);
        try{await loader.LoadAsync(ct=>services!.Bookings.ListAsync(request,ct),p=>{rows=p.Items;g.SetPage(p,EmployeeUi.BookingRow);foreach(DataGridViewRow row in g.Grid.Rows){var x=p.Items[row.Index];if(x.IsConfirmationOverdue)row.DefaultCellStyle.BackColor=Color.MistyRose;}});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys k){if(services is not null&&(k==Keys.F5||k==Keys.Enter&&filterPanelField0.Focused)){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref msg,k);}
}