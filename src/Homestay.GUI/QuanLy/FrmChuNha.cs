using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;
using Homestay.DTO.Homestays;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Manager;
/// <summary>Chủ nhà được chọn để chuyển sang màn hình tạo homestay.</summary>
public sealed record ListingOwnerSelection(int OwnerId,string OwnerName);
/// <summary>Màn hình chủ nhà: danh sách, thêm, sửa, đổi tài khoản ngân hàng, duyệt/tạm ngưng.</summary>
public partial class FrmOwners:ScreenForm
{
    private AppServices? services;private LiveGrid<OwnerItem>? live;private LiveGrid<HomestayItem>? homes;
    private ListingOwnerSelection? newlyCreatedOwner;private readonly Button createHomeButton=new(){Text="Tạo homestay cho chủ đang chọn",AutoSize=true,Enabled=false},createdHomeButton=new(){Text="Tạo homestay cho chủ vừa tạo",AutoSize=true,Enabled=false};private readonly Button bankButton=new(){Text="Đổi ngân hàng",AutoSize=true};
    public FrmOwners(){InitializeComponent();ListingDraftPreview.Add(toolbar,("Biểu mẫu chủ",()=>ListingDraftEditors.Owner()));HelpText="Tạo chủ chờ duyệt. CCCD và ngân hàng được che theo quyền. Đổi ngân hàng phải ghi nội dung đã gọi xác minh với chủ.";}
    public FrmOwners(AppServices s):this(){
        if(!s.Can(Permission.OwnerEdit))throw new ForbiddenException();services=s;
        LiveUi.Activate(toolbar,pendingNotice,reloadButton,addButton,editButton,approveButton,suspendButton);toolbar.Controls.AddRange([bankButton,createHomeButton,createdHomeButton]);createHomeButton.Visible=createdHomeButton.Visible=s.Can(Permission.HomestayCreate);createHomeButton.Click+=(_,_)=>{if(live?.Current is {} owner)s.Navigate("F15",new ListingOwnerSelection(owner.Id,owner.FullName));};createdHomeButton.Click+=(_,_)=>{if(newlyCreatedOwner is not null)s.Navigate("F15",newlyCreatedOwner);};
        LiveUi.EnumFilter<OwnerStatus>(filterPanelField1);grid0.SetColumns("Họ tên","SĐT","Trạng thái","Email","Ngân hàng","Tài khoản");grid0.SetSortKeys("FullName","Phone","Status",null,null,null);
        grid0.SetPreferenceIdentity(s.Session!.UserId.ToString(),"F22.owners");
        live=new(grid0,()=>{var text=filterPanelField0.Text;var status=LiveUi.EnumValue<OwnerStatus>(filterPanelField1);return(q,ct)=>s.Owners.ListAsync(new(){Page=q with{SearchText=text},Status=status},ct);},r=>[r.FullName,r.Phone,LiveUi.Status(r.Status),r.Email,r.BankName,r.BankAccountNo]);
        homes=new(grid1,()=>{var id=live.Current?.Id;return(q,ct)=>id is int owner?s.Homestays.ListAsync(new(){Page=q,OwnerId=owner},ct):Task.FromResult(new PagedResult<HomestayItem>{Items=[],PageNumber=1,PageSize=q.PageSize});},r=>[r.Name,r.AreaName,LiveUi.Status(r.Status)]);
        if(!s.Can(Permission.HomestayView))viewTabs.TabPages.Remove(tab1);
        addButton.Visible=s.Session.Position!=Position.Accountant;approveButton.Visible=suspendButton.Visible=bankButton.Visible=s.Session.Position==Position.Manager;
        live.Changed+=UpdateActions;reloadButton.Click+=async(_,_)=>await live.LoadAsync();viewTabs.SelectedIndexChanged+=async(_,_)=>{if(viewTabs.SelectedIndex==1)await homes.LoadAsync();};
        addButton.Click+=async(_,_)=>await RunMutationAsync(addButton,async()=>{using var d=ListingDraftEditors.Owner();if(d.ShowDialog(this)==DialogResult.OK){var created=await s.Owners.CreateAsync(Values(d));newlyCreatedOwner=new(created.Id,created.FullName);createdHomeButton.Enabled=true;createdHomeButton.Text="Tạo homestay cho "+created.FullName;await live.LoadAsync();}},enabledAfter:()=>true);
        editButton.Click+=async(_,_)=>await RunMutationAsync(editButton,async()=>{
            var r=await s.Owners.GetAsync(live.Selected.Id);using var d=ListingDraftEditors.Owner();
            foreach(var pair in new[]{("name",r.FullName),("phone",r.Phone),("email",r.Email),("address",r.Address),("identity",r.IsSensitiveMasked?null:r.IdNumber),("bank",r.BankName),("account",r.BankAccountNo),("accountName",r.BankAccountName),("note",r.Note)})LiveUi.Fill(d,pair.Item1,pair.Item2);
            foreach(var key in new[]{"bank","account","accountName"})d.Controls.Find(key,true).Single().Enabled=false;
            d.SetSummary("Để trống CCCD để giữ số cũ. Đổi ngân hàng dùng nút riêng sau khi gọi xác minh.");
            if(d.ShowDialog(this)==DialogResult.OK){await s.Owners.UpdateAsync(new(r.Id,Values(d) with{BankName=null,BankAccountNo=null,BankAccountName=null},r.RowVer));await live.LoadAsync();}
        },()=>live.LoadAsync(),()=>CanEdit());
        bankButton.Click+=async(_,_)=>await RunMutationAsync(bankButton,async()=>{
            var r=await s.Owners.GetAsync(live.Selected.Id);using var d=new FieldsDialog("Ngân hàng của "+r.FullName,new("bank","Ngân hàng",Initial:r.BankName,MaxLength:100),new("account","Số tài khoản",Initial:r.BankAccountNo,MaxLength:30),new("name","Tên chủ tài khoản",Initial:r.BankAccountName,MaxLength:100),new("note","Nội dung gọi xác minh",FieldKind.Multiline));
            if(d.ShowDialog(this)==DialogResult.OK){await s.Owners.ChangeBankAsync(new(r.Id,LiveUi.Text(d,"bank"),LiveUi.Text(d,"account"),LiveUi.Text(d,"name"),LiveUi.Text(d,"note"),r.RowVer));await live.LoadAsync();}
        },()=>live.LoadAsync(),()=>live.Current is not null);
        approveButton.Click+=async(_,_)=>await ChangeStatusAsync(false);suspendButton.Click+=async(_,_)=>await ChangeStatusAsync(true);
        grid0.DetailRequested+=(_,_)=>editButton.PerformClick();Shown+=async(_,_)=>await live.LoadAsync();Disposed+=(_,_)=>{live.Dispose();homes.Dispose();};UpdateActions();
    }
    private bool CanEdit()=>services?.Session?.Position!=Position.Accountant&&live?.Current is {} r&&(services?.Session?.Position==Position.Manager||r.Status==OwnerStatus.Pending);
    private void UpdateActions(){editButton.Enabled=CanEdit();var r=live?.Current;createHomeButton.Enabled=r is not null&&services?.Can(Permission.HomestayCreate)==true;approveButton.Enabled=r?.Status is OwnerStatus.Pending or OwnerStatus.Suspended or OwnerStatus.Rejected;suspendButton.Enabled=r?.Status==OwnerStatus.Approved;bankButton.Enabled=r is not null;approveButton.Text=r?.Status==OwnerStatus.Rejected?"Gửi lại hồ sơ":r?.Status==OwnerStatus.Suspended?"Mở lại":"Duyệt";}
    private Task ChangeStatusAsync(bool suspend)=>RunMutationAsync(suspend?suspendButton:approveButton,async()=>{var r=live!.Selected;var target=suspend?OwnerStatus.Suspended:r.Status==OwnerStatus.Rejected?OwnerStatus.Pending:OwnerStatus.Approved;var note=LiveUi.Note(this,LiveUi.Status(target),suspend);if(note is null)return;await services!.Owners.ChangeStatusAsync(new(r.Id,target,string.IsNullOrWhiteSpace(note)?null:note,r.RowVer));await live.LoadAsync();},()=>live!.LoadAsync(),()=>live?.Current is not null);
    private static CreateOwnerRequest Values(FieldsDialog d)=>new(){FullName=LiveUi.Text(d,"name"),Phone=LiveUi.Text(d,"phone"),Email=LiveUi.Optional(d,"email"),Address=LiveUi.Optional(d,"address"),IdNumber=LiveUi.Optional(d,"identity"),BankName=LiveUi.Optional(d,"bank"),BankAccountNo=LiveUi.Optional(d,"account"),BankAccountName=LiveUi.Optional(d,"accountName"),Note=LiveUi.Optional(d,"note")};
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&k==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}