using Homestay.BLL.Infrastructure;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Manager;
public partial class FrmCancellationApproval : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new(),details=new();
    private IReadOnlyList<CancelRequestItem> rows=[];
    private CancelRequestDetail? selected;
    private readonly int? bookingId;
    private readonly Label selectionStatus=new(){AutoSize=true,MaximumSize=new Size(700,0),Padding=new Padding(4,8,4,4)};
    private readonly TextBox search=new(){Width=230,PlaceholderText="Mã đơn / tên khách / người gửi"};
    private PagedGrid Current=>viewTabs.SelectedIndex==0?grid0:grid1;
    public FrmCancellationApproval(){
        InitializeComponent();HelpText="Người gửi không tự duyệt. Tiền hoàn dùng mức đã chốt lúc gửi, không tính lại theo ngày duyệt. Chỉ xử lý yêu cầu Hủy.";
        Disposed+=(_,_)=>{loader.Dispose();details.Dispose();};
    }
    public FrmCancellationApproval(AppServices services,int? bookingId=null):this(){
        if(!services.Can(Permission.BookingChangeReview))throw new ForbiddenException();
        this.services=services;this.bookingId=bookingId;reloadButton.Tag=approveButton.Tag=rejectButton.Tag=null;
        pendingNotice.Text=LifecycleUi.ReadOnlyNotice;pendingNotice.Visible=!LifecycleUi.CancellationAvailable;
        filterViewport.Visible=false;body.ColumnStyles[0].Width=0;toolbar.Controls.Add(search);toolbar.Controls.Add(selectionStatus);
        tab1.Text="Toàn bộ yêu cầu";approveButton.Enabled=rejectButton.Enabled=false;
        foreach(var g in new[]{grid0,grid1}){
            g.SetColumns("Mã đơn","Khách","Homestay","Người gửi","Lý do","Hoàn chốt lúc gửi","Trạng thái","Quyết định","Ngày gửi");
            g.SetSortKeys("BookingCode",null,null,null,null,null,"Status",null,"RequestedAtUtc");
            g.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F20."+g.Name);
            g.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);
            g.Grid.SelectionChanged+=async(_,_)=>{if(g==Current)await SelectAsync();};
            g.DetailRequested+=async(_,_)=>await ErrorHandler.RunAsync(reloadButton,async()=>{
                var d=await services.Changes.GetAsync(Selected().Id);var r=d.Request;
                MessageBox.Show(this,r.BookingCode+"\n"+r.Reason+"\nNgười gửi: "+r.RequestedByName+
                    "\nHoàn chốt lúc gửi: "+(r.QuotedRefundAmount is decimal amount?EmployeeUi.Money(amount):"Chưa có mức chốt")+
                    "\nNgười quyết định: "+r.DecidedByName+"\nGhi chú: "+r.DecisionNote,"Yêu cầu hủy");
            });
            g.ExportFilteredAsync=async ct=>{
                var all=new List<CancelRequestItem>();var q=Query(g.Query with{PageNumber=1,PageSize=100});
                while(true){var p=await services.Changes.ListAsync(q,ct);all.AddRange(p.Items);if(p.Items.Count==0||all.Count>=p.TotalCount)break;q=q with{Page=q.Page with{PageNumber=q.Page.PageNumber+1}};}
                return EmployeeUi.Table(g,all.Select(Map));
            };
        }
        reloadButton.Click+=async(_,_)=>await LoadAsync(Current.Query with{PageNumber=1});
        viewTabs.SelectedIndexChanged+=async(_,_)=>await LoadAsync(Current.Query with{PageNumber=1});
        approveButton.Click+=async(_,_)=>await ReviewAsync(false);
        rejectButton.Click+=async(_,_)=>await ReviewAsync(true);
        Shown+=async(_,_)=>await LoadAsync(Current.Query);
    }
    private CancelRequestItem Selected()=>rows[EmployeeUi.Selected(Current)];
    private ChangeRequestSearchRequest Query(PageRequest page)=>new(){Page=page with{SearchText=search.Text},BookingId=bookingId,Status=viewTabs.SelectedIndex==0?ChangeRequestStatus.Pending:null};
    private static object?[] Map(CancelRequestItem r)=>[r.BookingCode,r.CustomerName,r.HomestayName,r.RequestedByName,r.Reason,
        r.QuotedRefundAmount is decimal amount?EmployeeUi.Money(amount):"Chưa có mức chốt",EmployeeUi.ChangeStatus(r.Status),r.DecisionNote,EmployeeUi.Time(r.RequestedAtUtc)];
    private async Task LoadAsync(PageRequest q){
        details.CancelPending();selected=null;approveButton.Enabled=rejectButton.Enabled=false;rows=[];selectionStatus.Text="Đang tải yêu cầu…";var grid=Current;
        try{await loader.LoadAsync(ct=>services!.Changes.ListAsync(Query(q),ct),p=>{rows=p.Items;grid.SetPage(p,Map);if(rows.Count==0)selectionStatus.Text="Không có yêu cầu phù hợp.";});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private async Task SelectAsync(){
        details.CancelPending();selected=null;approveButton.Enabled=rejectButton.Enabled=false;selectionStatus.Text="Chọn yêu cầu để xem quyền xử lý.";
        if(Current.Grid.SelectedRows.Count!=1)return;var index=Current.Grid.SelectedRows[0].Index;if(index>=rows.Count)return;var id=rows[index].Id;
        try{await details.LoadAsync(ct=>services!.Changes.GetAsync(id,ct),d=>{selected=d;UpdateSelection();});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private void UpdateSelection(){
        var request=selected?.Request;
        approveButton.Enabled=rejectButton.Enabled=LifecycleUi.CancellationAvailable&&request?.CanReview==true;
        selectionStatus.Text=request is null?"Chọn yêu cầu để xem quyền xử lý.":request.IsRequestedByCurrentUser
            ?"Bạn là người gửi yêu cầu; cần Quản lý khác duyệt hoặc từ chối."
            :request.Status!=ChangeRequestStatus.Pending?"Yêu cầu đã xử lý: "+EmployeeUi.ChangeStatus(request.Status)
            :"Hoàn chốt: "+(request.QuotedRefundAmount is decimal amount?EmployeeUi.Money(amount):"Chưa có mức chốt")+" · Người gửi: "+request.RequestedByName;
    }
    public Task RefreshFromServerAsync()=>LoadAsync(Current.Query);
    private Task ReviewAsync(bool reject)=>RunMutationAsync(reject?rejectButton:approveButton,async()=>{
        var current=selected;
        if(current is null||!current.Request.CanReview)return;
        using var dialog=new FieldsDialog((reject?"Từ chối yêu cầu ":"Duyệt hủy ")+current.Request.BookingCode,
            new FieldSpec("note",reject?"Lý do từ chối":"Ghi chú duyệt — tiền hoàn đã chốt: "+(current.Request.QuotedRefundAmount is decimal amount?EmployeeUi.Money(amount):"Chưa có mức chốt"),FieldKind.Multiline,Required:reject));
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var r=current.Request;var version=current.Booking.Booking.RowVer;var note=(string?)dialog.Values["note"];
        if(reject)await services!.Changes.RejectAsync(new(r.Id,note!,r.RowVer,version));
        else await services!.Changes.ApproveAsync(new(r.Id,r.RowVer,version,note));
        await LoadAsync(Current.Query);
    },()=>LoadAsync(Current.Query),()=>LifecycleUi.CancellationAvailable&&selected?.Request.CanReview==true);
    protected override bool ProcessCmdKey(ref Message m,Keys k){
        if(services is not null&&(k==Keys.F5||k==Keys.Enter&&search.Focused)){reloadButton.PerformClick();return true;}
        if(k==(Keys.Control|Keys.F)){search.Focus();return true;}return base.ProcessCmdKey(ref m,k);
    }
}
