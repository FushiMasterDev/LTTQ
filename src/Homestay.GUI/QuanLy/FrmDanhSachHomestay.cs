using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Manager;
/// <summary>Màn hình danh sách homestay: lọc theo trạng thái/khu vực/chủ, tạo mới và mở hồ sơ.</summary>
public partial class FrmHomestays:ScreenForm
{
    private ListingOwnerSelection? selectedOwner;private readonly SearchPicker ownerFilter=new(){Width=230};private AppServices? services;private LiveGrid<HomestayItem>? main,pending;
    private readonly AsyncLoader actionLoader=new();private bool canSubmit;private LiveGrid<HomestayItem> Current=>viewTabs.SelectedIndex==0?main!:pending!;
    public FrmHomestays(){InitializeComponent();ListingDraftPreview.Add(toolbar,("Biểu mẫu homestay",()=>ListingDraftEditors.Homestay([],[])));HelpText="Lọc theo khu vực và trạng thái. Tạo nháp rồi nhập phòng, giá, tiện nghi, ảnh và giấy tờ tại Chi tiết trước khi gửi duyệt.";}
    public FrmHomestays(AppServices s,ListingOwnerSelection? owner=null):this(){selectedOwner=owner;
        if(!s.Can(Permission.HomestayView))throw new ForbiddenException();services=s;LiveUi.Activate(toolbar,pendingNotice,reloadButton,addButton,detailButton,submitButton);
        toolbar.Controls.Add(new Label{Text="Chủ homestay",AutoSize=true,Padding=new Padding(3,8,3,0)});toolbar.Controls.Add(ownerFilter);LiveUi.EnumFilter<HomestayStatus>(filterPanelField2);grid0.SetColumns("Tên","Chủ","Khu vực","Trạng thái","Hợp đồng","Hoa hồng","Hạn","Loại phòng","Số phòng");grid0.SetSortKeys("Name","OwnerName","AreaName","Status",null,null,null,null,null);
        grid1.SetColumns("Tên","Chủ","Khu vực","Trạng thái");grid1.SetSortKeys("Name","OwnerName","AreaName","Status");
        main=new(grid0,()=>Read(false),r=>[r.Name,r.OwnerName,r.AreaName,LiveUi.Status(r.Status),r.ActiveContractNo,r.CommissionRate?.ToString("P0"),r.ContractEndDate?.ToString("dd/MM/yyyy"),r.RoomTypeCount,r.RoomCount]);
        pending=new(grid1,()=>Read(true),r=>[r.Name,r.OwnerName,r.AreaName,LiveUi.Status(r.Status)]);
        if(!s.Can(Permission.HomestayReview))viewTabs.TabPages.Remove(tab1);
        main.Changed+=UpdateActions;pending.Changed+=UpdateActions;
        reloadButton.Click+=async(_,_)=>await Current.LoadAsync();viewTabs.SelectedIndexChanged+=async(_,_)=>await Current.LoadAsync();
        detailButton.Click+=(_,_)=>{if(Current.Current is {} r)s.Navigate("F16",r.Id);};grid0.DetailRequested+=(_,_)=>detailButton.PerformClick();grid1.DetailRequested+=(_,_)=>detailButton.PerformClick();
        addButton.Click+=async(_,_)=>await RunMutationAsync(addButton,async()=>{
            var owners=(await s.Owners.LookupAsync()).ToList();if(selectedOwner is {} known&&!owners.Any(o=>o.Id==known.OwnerId))owners.Add(new(){Id=known.OwnerId,Name=known.OwnerName});using var d=ListingDraftEditors.Homestay(owners,await s.Homestays.ListAreasAsync());if(selectedOwner is {} initial){LiveUi.Fill(d,"owner",initial.OwnerId);d.SetSummary("Đang dùng mã chủ đã nhận từ bước tạo/chọn chủ. Chủ vừa duyệt có thể chưa nằm trong lookup của Nhân viên khi chưa có homestay. Dịch vụ vẫn kiểm chủ tồn tại và khu vực trước khi lưu.");}if(d.ShowDialog(this)!=DialogResult.OK)return;
            var created=await s.Homestays.CreateDraftAsync(Values(d));await Current.LoadAsync();s.Navigate("F16",created.Homestay.Id);
        },enabledAfter:()=>s.Can(Permission.HomestayCreate));
        submitButton.Click+=async(_,_)=>await RunMutationAsync(submitButton,async()=>{var d=await s.Homestays.GetAsync(Current.Selected.Id);await s.Homestays.SubmitAsync(new(d.Homestay.Id,d.Homestay.RowVer));await Current.LoadAsync();},()=>Current.LoadAsync(),()=>canSubmit);
        Shown+=async(_,_)=>{try{filterPanelField1.SetItems(await s.Homestays.ListAreasAsync());ownerFilter.SetItems(await s.Owners.LookupAsync());await Current.LoadAsync();}catch(Exception e){Homestay.GUI.Infrastructure.ErrorHandler.Show(e,this);}};
        Disposed+=(_,_)=>{actionLoader.Dispose();main.Dispose();pending.Dispose();};UpdateActions();
    }
    private Func<PageRequest,CancellationToken,Task<PagedResult<HomestayItem>>> Read(bool awaiting){var text=filterPanelField0.Text;var area=filterPanelField1.SelectedItem?.Id;var status=LiveUi.EnumValue<HomestayStatus>(filterPanelField2);var owner=ownerFilter.SelectedItem?.Id;return(q,ct)=>services!.Homestays.ListAsync(new(){Page=q with{SearchText=text},AreaId=area,Status=awaiting?HomestayStatus.PendingReview:status,OwnerId=owner,PendingReviewOnly=awaiting},ct);}
    private void UpdateActions(){detailButton.Enabled=Current.Current is not null;addButton.Enabled=services?.Can(Permission.HomestayCreate)==true;actionLoader.CancelPending();canSubmit=submitButton.Enabled=false;if(Current.Current is {} row)_=LoadSubmitActionsAsync(row.Id);}
    private async Task LoadSubmitActionsAsync(int id){try{await actionLoader.LoadAsync(ct=>services!.Homestays.GetAsync(id,ct),d=>{canSubmit=d.AllowedActions.Contains(HomestayAction.Submit);submitButton.Enabled=canSubmit;});}catch(Exception e){if(!IsDisposed)Homestay.GUI.Infrastructure.ErrorHandler.Show(e,this);}}internal static CreateHomestayRequest Values(FieldsDialog d)=>new(){OwnerId=LiveUi.Id(d,"owner"),AreaId=LiveUi.Id(d,"area"),Name=LiveUi.Text(d,"name"),Address=LiveUi.Text(d,"address"),ContactPhone=LiveUi.Optional(d,"phone"),Description=LiveUi.Optional(d,"description"),CheckinGuide=LiveUi.Optional(d,"guide"),CheckinTime=(TimeOnly)d.Values["checkin"]!,CheckoutTime=(TimeOnly)d.Values["checkout"]!};
    public Task RefreshFromServerAsync()=>Current.LoadAsync();
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&k==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}