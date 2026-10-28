using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Contracts;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;
namespace Homestay.GUI.Manager;
/// <summary>Màn hình hợp đồng chủ nhà: tạo, kích hoạt, gia hạn, chấm dứt.</summary>
public partial class FrmContracts:ScreenForm
{
    private readonly Button validateButton=new(){Text="Kiểm tra lại hiệu lực",AutoSize=true,Enabled=false};private AppServices? services;private LiveGrid<ContractItem>? live;
    public FrmContracts(){InitializeComponent();ListingDraftPreview.Add(toolbar,("Biểu mẫu hợp đồng",()=>ListingDraftEditors.Contract([],EmployeeUi.Today)));HelpText="Hợp đồng tạo mới có hiệu lực ngay. Kiểm tra lại hiệu lực chỉ xác nhận hợp đồng Active theo điều kiện hiện tại; không kích hoạt lại hợp đồng đã chấm dứt. Hoa hồng 10–15%. Gia hạn chỉ lùi ngày kết thúc; chấm dứt phải ghi lý do.";}
    public FrmContracts(AppServices s):this(){
        if(!s.Can(Permission.ContractManage))throw new ForbiddenException();services=s;LiveUi.Activate(toolbar,pendingNotice,reloadButton,addButton,renewButton,endButton);
        toolbar.Controls.Add(validateButton);LiveUi.EnumFilter<ContractStatus>(filterPanelField2);filterPanelField3.ShowCheckBox=filterPanelField4.ShowCheckBox=true;filterPanelField3.Checked=filterPanelField4.Checked=false;
        filterPanelField3.Visible=filterPanelField4.Visible=filterPanelField3Label.Visible=filterPanelField4Label.Visible=false;
        grid0.SetColumns("Số","Homestay","Bắt đầu","Kết thúc","Hoa hồng","Trạng thái");grid0.SetSortKeys("ContractNo","HomestayName","StartDate","EndDate",null,"Status");
        live=new(grid0,()=>{var text=filterPanelField0.Text;var home=filterPanelField1.SelectedItem?.Id;var state=LiveUi.EnumValue<ContractStatus>(filterPanelField2);return(q,ct)=>s.Contracts.ListAsync(new(){Page=q with{SearchText=text},HomestayId=home,Status=state},ct);},r=>[r.ContractNo,r.HomestayName,EmployeeUi.Date(r.StartDate),EmployeeUi.Date(r.EndDate),r.CommissionRate.ToString("P2"),LiveUi.Status(r.Status)]);
        live.Changed+=()=>validateButton.Enabled=renewButton.Enabled=endButton.Enabled=live.Current?.Status==ContractStatus.Active;validateButton.Click+=async(_,_)=>await RunMutationAsync(validateButton,async()=>{var r=await s.Contracts.GetAsync(live.Selected.Id);await s.Contracts.ActivateAsync(new(r.Id,r.RowVer));await s.Homestays.GetAsync(r.HomestayId);await live.LoadAsync();},()=>live.LoadAsync(),()=>live.Current?.Status==ContractStatus.Active);
        reloadButton.Click+=async(_,_)=>await live.LoadAsync();
        addButton.Click+=async(_,_)=>await RunMutationAsync(addButton,async()=>{using var d=ListingDraftEditors.Contract(await s.Contracts.ListHomestaysAsync(),DateOnly.FromDateTime((await s.ServerNowAsync()).AddHours(7)));if(d.ShowDialog(this)!=DialogResult.OK)return;var h=await s.Homestays.GetAsync(LiveUi.Id(d,"homestay"));await s.Contracts.CreateAsync(new(h.Homestay.Id,LiveUi.Text(d,"number"),(DateOnly)d.Values["start"]!,(DateOnly)d.Values["end"]!,(decimal)d.Values["commissionPercent"]!/100m,h.Homestay.RowVer));await live.LoadAsync();},()=>live.LoadAsync());
        renewButton.Click+=async(_,_)=>await RunMutationAsync(renewButton,async()=>{var r=await s.Contracts.GetAsync(live.Selected.Id);using var d=new FieldsDialog("Gia hạn "+r.ContractNo,new FieldSpec("end","Ngày kết thúc mới",FieldKind.Date,r.EndDate.AddYears(1)));if(d.ShowDialog(this)==DialogResult.OK){await s.Contracts.ExtendAsync(new(r.Id,(DateOnly)d.Values["end"]!,r.RowVer));await s.Homestays.GetAsync(r.HomestayId);await live.LoadAsync();}},()=>live.LoadAsync(),()=>live.Current?.Status==ContractStatus.Active);
        endButton.Click+=async(_,_)=>await RunMutationAsync(endButton,async()=>{var r=await s.Contracts.GetAsync(live.Selected.Id);var note=LiveUi.Note(this,"Chấm dứt "+r.ContractNo);if(note is not null){await s.Contracts.TerminateAsync(new(r.Id,note,r.RowVer));await s.Homestays.GetAsync(r.HomestayId);await live.LoadAsync();}},()=>live.LoadAsync(),()=>live.Current?.Status==ContractStatus.Active);
        Shown+=async(_,_)=>{try{filterPanelField1.SetItems(await s.Contracts.ListHomestaysAsync());await live.LoadAsync();}catch(Exception e){Homestay.GUI.Infrastructure.ErrorHandler.Show(e,this);}};Disposed+=(_,_)=>live.Dispose();renewButton.Enabled=endButton.Enabled=false;
    }
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&k==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}