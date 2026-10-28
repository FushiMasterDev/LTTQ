using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Files;
using Homestay.DTO.Payments;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;
/// <summary>Màn hình kế toán xác nhận tiền cọc: xem phiếu thu và chứng từ, xác nhận hoặc từ chối.</summary>
public partial class FrmDepositVerification : ScreenForm
{
    private AppServices? services;
    private readonly AsyncLoader loader=new(),details=new();
    private IReadOnlyList<PaymentQueueItem> rows=[];
    private ReceiptVerificationDetail? selected;
    private EvidenceFile? evidence;
    public FrmDepositVerification(){
        InitializeComponent();HelpText="Đối chiếu số đã chuyển với số cần thu và chứng từ. Người ghi không tự xác minh. Các thao tác ghi chỉ mở khi dịch vụ đợt 3 đã nghiệm thu.";
        Disposed+=(_,_)=>{loader.Dispose();details.Dispose();filterPanelField0.Image?.Dispose();};
    }
    public FrmDepositVerification(AppServices services):this(){
        if(!services.Can(Permission.PaymentVerify))throw new ForbiddenException();
        this.services=services;
        foreach(var button in new[]{reloadButton,zoomButton,verifyButton,rejectButton})button.Tag=null;
        pendingNotice.Text=LifecycleUi.ReadOnlyNotice;pendingNotice.Visible=!LifecycleUi.ReceiptReviewAvailable;
        matchButton.Visible=false;viewTabs.TabPages.Remove(tab1);zoomButton.Text="Xem / lưu chứng từ";
        body.Controls.Remove(content);body.Controls.Remove(filterViewport);body.Controls.Add(content,0,0);body.Controls.Add(filterViewport,1,0);
        body.ColumnStyles[0].SizeType=SizeType.Percent;body.ColumnStyles[0].Width=65;
        body.ColumnStyles[1].SizeType=SizeType.Percent;body.ColumnStyles[1].Width=35;
        filterPanelField3.Multiline=true;filterPanelField3.Height=190;filterPanelField3.ScrollBars=ScrollBars.Vertical;
        verifyButton.Enabled=rejectButton.Enabled=zoomButton.Enabled=false;
        filterPanelField1.ReadOnly=filterPanelField2.ReadOnly=filterPanelField3.ReadOnly=true;
        grid0.SetColumns("Số phiếu","Mã đơn","Khách","Homestay","Cần thu","Đã chuyển","Người ghi","Ghi lúc");
        grid0.SetSortKeys("VoucherNo","BookingCode",null,null,null,"Amount",null,"RecordedAtUtc");
        grid0.SetPreferenceIdentity(services.Session!.UserId.ToString(),"F27.pending");
        grid0.QueryRequested+=async(_,e)=>await LoadAsync(e.Request);
        grid0.Grid.SelectionChanged+=async(_,_)=>await SelectAsync();
        grid0.DetailRequested+=(_,_)=>{if(selected is not null)services.Navigate("F09",selected.Receipt.BookingId);};
        reloadButton.Click+=async(_,_)=>await LoadAsync(grid0.Query with{PageNumber=1});
        zoomButton.Click+=(_,_)=>{try{if(evidence is not null)EvidencePresentation.Show(this,evidence,"Chứng từ thu cọc");}catch(Exception e){ErrorHandler.Show(e,this);}};
        verifyButton.Click+=async(_,_)=>await RunMutationAsync(verifyButton,async()=>{
            if(selected is null)return;
            await services.Payments.VerifyReceiptAsync(new(selected.Receipt.Id,selected.Receipt.RowVer,selected.Booking.Booking.RowVer));
            await LoadAsync(grid0.Query);
        },()=>LoadAsync(grid0.Query),()=>LifecycleUi.ReceiptReviewAvailable&&selected?.CanVerify==true);
        rejectButton.Click+=async(_,_)=>await RunMutationAsync(rejectButton,async()=>{
            if(selected is null)return;
            using var dialog=new FieldsDialog("Từ chối phiếu thu",new FieldSpec("reason","Lý do",FieldKind.Multiline));
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            await services.Payments.RejectReceiptAsync(new(selected.Receipt.Id,(string)dialog.Values["reason"]!,selected.Receipt.RowVer,selected.Booking.Booking.RowVer));
            await LoadAsync(grid0.Query);
        },()=>LoadAsync(grid0.Query),()=>LifecycleUi.ReceiptReviewAvailable&&selected?.CanReject==true);
        grid0.ExportFilteredAsync=async ct=>{
            var items=new List<PaymentQueueItem>();var q=grid0.Query with{PageNumber=1,PageSize=100};
            while(true){var page=await services.Payments.ListPendingReceiptsAsync(new(){Page=q},ct);items.AddRange(page.Items);if(page.Items.Count==0||items.Count>=page.TotalCount)break;q=q with{PageNumber=q.PageNumber+1};}
            return EmployeeUi.Table(grid0,items.Select(Map));
        };
        Shown+=async(_,_)=>await LoadAsync(grid0.Query);
    }
    private static object?[] Map(PaymentQueueItem x)=>[x.VoucherNo,x.BookingCode,x.CustomerName,x.HomestayName,EmployeeUi.Money(x.AmountDue),EmployeeUi.Money(x.Amount),x.RecordedByName,EmployeeUi.Time(x.RecordedAtUtc)];
    private void ClearDetail(){
        selected=null;evidence=null;verifyButton.Enabled=rejectButton.Enabled=zoomButton.Enabled=false;
        filterPanelField0.Image?.Dispose();filterPanelField0.Image=null;
        filterPanelField1.Clear();filterPanelField2.Clear();filterPanelField3.Clear();
    }
    private async Task LoadAsync(PageRequest q){
        details.CancelPending();ClearDetail();rows=[];
        try{await loader.LoadAsync(ct=>services!.Payments.ListPendingReceiptsAsync(new(){Page=q},ct),p=>{rows=p.Items;grid0.SetPage(p,Map);});}
        catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    private async Task SelectAsync(){
        details.CancelPending();ClearDetail();
        if(services is null||grid0.Grid.SelectedRows.Count!=1)return;
        var index=grid0.Grid.SelectedRows[0].Index;if(index>=rows.Count)return;
        var id=rows[index].Id;
        try{await details.LoadAsync(async ct=>{
            var detail=await services.Payments.GetReceiptForVerificationAsync(id,ct);
            var file=detail.Receipt.EvidenceFileId is Guid fileId?await services.Files.GetEvidenceAsync(detail.Receipt.BookingId,fileId,ct):null;
            return (Detail:detail,File:file);
        },data=>{
            selected=data.Detail;evidence=data.File;var r=selected.Receipt;
            filterPanelField1.Text=r.BookingCode;filterPanelField2.Text=EmployeeUi.Money(r.AmountDue);
            filterPanelField3.Text="Đã chuyển: "+EmployeeUi.Money(r.Amount)+
                "\r\nKhách chuyển lúc: "+EmployeeUi.Time(r.TransferredAtUtc)+"\r\nGhi lúc: "+EmployeeUi.Time(r.RecordedAtUtc)+
                "\r\nNgười ghi: "+r.RecordedByName+(r.IsRecordedByCurrentUser?" (Bạn là người ghi)":"")+
                "\r\nNgười chuyển: "+r.PayerOrPayeeName+" · "+r.MaskedPayerAccount+"\r\nNội dung: "+r.TransferContent+
                (selected.IsInsufficient?"\r\nTHIẾU TIỀN - chưa đủ điều kiện xác minh":"")+
                "\r\nLượt từ chối tiếp: "+selected.RejectionNumber+"/"+selected.MaxReceiptRejections;
            filterPanelField0Label.Text=evidence is null?"Không có chứng từ":evidence.ContentType=="application/pdf"?"Chứng từ PDF":evidence.ContentType=="image/webp"?"Chứng từ WEBP":"Ảnh chứng từ";
            if(evidence?.ContentType is "image/jpeg" or "image/png"){
                try{using var stream=new MemoryStream(evidence.Content);using var image=Image.FromStream(stream);filterPanelField0.Image=new Bitmap(image);}
                catch(ArgumentException){filterPanelField0Label.Text="Ảnh không đọc được - chọn Xem / lưu chứng từ";}
            }
            zoomButton.Enabled=evidence is not null;
            verifyButton.Enabled=LifecycleUi.ReceiptReviewAvailable&&selected.CanVerify;
            rejectButton.Enabled=LifecycleUi.ReceiptReviewAvailable&&selected.CanReject;
        });}catch(Exception e){if(!IsDisposed)ErrorHandler.Show(e,this);}
    }
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&k==Keys.F5){reloadButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}
