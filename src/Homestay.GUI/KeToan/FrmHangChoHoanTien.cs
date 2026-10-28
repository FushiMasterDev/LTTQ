using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;
/// <summary>Màn hình hàng chờ hoàn tiền của kế toán: xem khoản chờ chi, cập nhật tài khoản nhận, ghi đã chuyển hoàn.</summary>
public partial class FrmRefundQueue : ScreenForm
{
    private AppServices? services;
    private readonly Button accountButton=new(){Text="Thông tin nhận hoàn",AutoSize=true};
    private readonly AsyncLoader loader=new();
    private IReadOnlyList<PaymentQueueItem> rows=[];
    private readonly Label selectionStatus=new(){AutoSize=true,MaximumSize=new Size(700,0),Padding=new Padding(4,8,4,4)};
    public FrmRefundQueue(){
        InitializeComponent();HelpText="Hàng chờ hoàn tiền thật. Không có khoản bù/cọc bổ sung. Ghi đã chi cần mã giao dịch, thời điểm và phiên bản phiếu; chứng từ là tùy chọn.";
        Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmRefundQueue(AppServices services):this(){
        if(!services.Can(Permission.BookingRefundRecord))throw new ForbiddenException();
        this.services=services;reloadButton.Tag=payButton.Tag=proofButton.Tag=null;
        pendingNotice.Text="Chỉ ghi nhận sau khi đã chi thực tế. Người chi phải khác người gửi, duyệt hủy và lập phiếu hoàn.";pendingNotice.Visible=true;toolbar.Controls.Add(selectionStatus);
        filterPanelField1.Visible=filterPanelField1Label.Visible=filterPanelField2.Visible=filterPanelField2Label.Visible=false;
        filterPanelField0Label.Text="Mã phiếu / đơn / khách / homestay";
        grid0.SetColumns("Số phiếu","Mã đơn","Khách","Homestay","Số tiền hoàn","Người nhận","Tài khoản nhận","Lý do hủy","Tạo lúc");
        grid0.SetSortKeys("VoucherNo","BookingCode",null,null,"Amount",null,null,null,"RecordedAtUtc");
        grid0.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F29.refunds");
        grid0.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);
        grid0.Grid.SelectionChanged+=(_,_)=>{
            var item=SelectedOrNull();proofButton.Enabled=item?.EvidenceFileId is not null;
            payButton.Enabled=CanPaySelected();
            selectionStatus.Text=item is null?"Chọn phiếu cần ghi nhận.":item.IsRecordedByCurrentUser?"Bạn là người lập phiếu; cần Kế toán khác chi.":string.IsNullOrWhiteSpace(item.RefundAccount)?"Chưa lưu tài khoản nhận. Đối chiếu người nhận từ chứng từ chi thực tế.":"Người nhận: "+item.PayerOrPayeeName;
        };
        grid0.DetailRequested+=(_,_)=>{var item=SelectedOrNull();if(item is not null)services.Navigate("F09",item.BookingId);};
        reloadButton.Click+=async(_,_)=>await LoadAsync(grid0.Query with{PageNumber=1});
        proofButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(proofButton,async()=>{
            var item=SelectedOrNull()??throw new BusinessException("Vui lòng chọn phiếu.");
            if(item.EvidenceFileId is not Guid id)throw new BusinessException("Phiếu chưa có chứng từ.");
            var file=await services.Files.GetEvidenceAsync(item.BookingId,id);if(!IsDisposed)EvidencePresentation.Show(this,file,"Chứng từ hoàn tiền");
        },enabledAfter:()=>SelectedOrNull()?.EvidenceFileId is not null);
        payButton.Click+=async(_,_)=>await RunMutationAsync(payButton,PayAsync,()=>LoadAsync(grid0.Query),CanPaySelected);
        toolbar.Controls.Add(accountButton);accountButton.Enabled=false;
        grid0.Grid.SelectionChanged+=(_,_)=>accountButton.Enabled=SelectedOrNull() is not null;
        accountButton.Click+=async(_,_)=>await RunMutationAsync(accountButton,async()=>{
            var item=SelectedOrNull()??throw new BusinessException("Vui lòng chọn phiếu.");
            using var d=new FieldsDialog("Thông tin nhận hoàn "+item.VoucherNo,new("name","Tên người nhận",Initial:item.PayerOrPayeeName??item.CustomerName,MaxLength:100),new("account","Tài khoản nhận",MaxLength:30));
            d.SetSummary("Đối chiếu tài khoản với khách trước khi lưu. Lưu tài khoản chưa ghi nhận đã chi.");
            if(d.ShowDialog(this)==DialogResult.OK){await services.Payments.UpdateRefundAccountAsync(new(item.Id,LiveUi.Text(d,"account"),LiveUi.Text(d,"name"),item.RowVer));await LoadAsync(grid0.Query);}
        },()=>LoadAsync(grid0.Query),()=>SelectedOrNull() is not null);
        grid0.ExportFilteredAsync=async ct=>{
            var all=new List<PaymentQueueItem>();var q=Query(grid0.Query with{PageNumber=1,PageSize=100});
            while(true){var p=await services.Payments.ListPendingRefundsAsync(q,ct);all.AddRange(p.Items);if(p.Items.Count==0||all.Count>=p.TotalCount)break;q=q with{Page=q.Page with{PageNumber=q.Page.PageNumber+1}};}
            return EmployeeUi.Table(grid0,all.Select(Map));
        };
        payButton.Enabled=proofButton.Enabled=false;Shown+=async(_,_)=>await LoadAsync(grid0.Query);
    }
    private bool CanPaySelected(){
        var item=SelectedOrNull();
        return LifecycleUi.RefundPaymentAvailable&&item is {Status:PaymentStatus.Pending,Type:PaymentType.Refund,IsRecordedByCurrentUser:false};
    }
    private PaymentQueueRequest Query(PageRequest q)=>new(){Page=q with{SearchText=filterPanelField0.Text}};
    private PaymentQueueItem? SelectedOrNull()=>grid0.Grid.SelectedRows.Count==1&&grid0.Grid.SelectedRows[0].Index<rows.Count?rows[grid0.Grid.SelectedRows[0].Index]:null;
    private static object?[] Map(PaymentQueueItem r)=>[r.VoucherNo,r.BookingCode,r.CustomerName,r.HomestayName,EmployeeUi.Money(r.Amount),r.PayerOrPayeeName,r.RefundAccount??"Chưa có tài khoản nhận",r.CancelReason,EmployeeUi.Time(r.RecordedAtUtc)];
    private async Task LoadAsync(PageRequest q){
        rows=[];accountButton.Enabled=payButton.Enabled=proofButton.Enabled=false;selectionStatus.Text="Đang tải phiếu hoàn…";
        try{await loader.LoadAsync(ct=>services!.Payments.ListPendingRefundsAsync(Query(q),ct),p=>{rows=p.Items;grid0.SetPage(p,Map);if(rows.Count==0)selectionStatus.Text="Không có phiếu chờ chi phù hợp.";});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private async Task PayAsync(){
        var item=SelectedOrNull()??throw new BusinessException("Vui lòng chọn phiếu.");
        using var dialog=RefundPaymentEditor.Create(item,await services!.ServerNowAsync());
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        if(dialog.Values["confirmed"] is not true)throw new BusinessException("Chỉ ghi nhận đã chi sau khi đối chiếu giao dịch thực tế.");
        var time=(TimeOnly)dialog.Values["time"]!;
        Homestay.DTO.Files.EvidenceFile? proof=null;
        if((bool)dialog.Values["attach"]!){
            using var fileDialog=new OpenFileDialog{Filter=EvidencePresentation.FileFilter};
            if(fileDialog.ShowDialog(this)!=DialogResult.OK)return;
            proof=await EvidencePresentation.ReadAsync(fileDialog.FileName);
        }
        var paid=((DateOnly)dialog.Values["date"]!).ToDateTime(time).AddHours(-7);
        await services.Payments.RecordRefundPaidAsync(new(){PaymentId=item.Id,RowVer=item.RowVer,BankRef=(string)dialog.Values["reference"]!,PaidAtUtc=DateTime.SpecifyKind(paid,DateTimeKind.Utc),EvidenceBytes=proof?.Content,EvidenceFileName=proof?.FileName});
        await LoadAsync(grid0.Query);
    }
    public Task RefreshFromServerAsync()=>LoadAsync(grid0.Query);
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&(k==Keys.F5||k==Keys.Enter&&filterPanelField0.Focused)){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}
