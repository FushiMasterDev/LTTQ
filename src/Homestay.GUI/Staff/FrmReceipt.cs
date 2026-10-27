using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmReceipt : ScreenForm
{
    private AppServices? services;
    private BookingDetail? booking;
    private byte[]? evidence;
    private string? evidenceName;
    private int bookingId;
    private readonly Button previewEvidence=new(){Text="Xem chứng từ",AutoSize=true,Enabled=false};
    private readonly Button clearEvidence=new(){Text="Bỏ chứng từ",AutoSize=true,Enabled=false};
    private Homestay.DTO.Files.EvidenceFile? evidenceFile;
    public FrmReceipt(){InitializeComponent();HelpText="Chuyển khoản bắt buộc có chứng từ JPG, PNG, WEBP hoặc PDF tối đa 5 MB. Số tiền và thời điểm khách chuyển được kiểm tra ở máy chủ. Phiếu lưu ở trạng thái Chờ xác minh."; }
    public FrmReceipt(AppServices services,int bookingId):this(){
        if(!services.Can(Permission.PaymentRecord))throw new ForbiddenException();
        this.services=services;this.bookingId=bookingId;EmployeeUi.Activate(pendingNotice,fileButton,saveButton);saveButton.Enabled=false;
        editorFieldsField1.Minimum=1;editorFieldsField1.Maximum=999999999999999999m;editorFieldsField1.DecimalPlaces=0;
        editorFieldsField2.CustomFormat="dd/MM/yyyy HH:mm";editorFieldsField2.Value=DateTime.UtcNow.AddHours(7);
        editorFieldsField2Label.Text="Khách chuyển / nhận tiền lúc (giờ Việt Nam)";
        editorFieldsField3.SelectedIndex=0;editorFieldsField7.ReadOnly=true;
        toolbar.Controls.AddRange([previewEvidence,clearEvidence]);
        previewEvidence.Click+=(_,_)=>{try{if(evidenceFile is not null)EvidencePresentation.Show(this,evidenceFile,"Chứng từ thu cọc");}catch(Exception e){ErrorHandler.Show(e,this);}};
        clearEvidence.Click+=(_,_)=>{evidence=null;evidenceName=null;evidenceFile=null;editorFieldsField7.Clear();previewEvidence.Enabled=clearEvidence.Enabled=false;};
        fileButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(fileButton,async()=>{
            using var d=new OpenFileDialog{Filter=EvidencePresentation.FileFilter};
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            var selected=await EvidencePresentation.ReadAsync(d.FileName);
            if(IsDisposed)return;
            evidenceFile=selected;evidence=selected.Content;evidenceName=selected.FileName;editorFieldsField7.Text=evidenceName;
            previewEvidence.Enabled=clearEvidence.Enabled=true;
        });
        saveButton.Click+=async(_,_)=>await RunMutationAsync(saveButton,SaveAsync,LoadAsync,()=>booking?.AllowedActions.Contains(BookingAction.RecordDeposit)==true);
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(fileButton,LoadAsync);
    }
    private async Task LoadAsync(){
        booking=await services!.Bookings.GetAsync(bookingId);
        editorFieldsField2.Value=(await services.ServerNowAsync()).AddHours(7);
        editorFieldsField0.Text=booking.Booking.Code;editorFieldsField1.Value=Math.Clamp(booking.AmountDue,editorFieldsField1.Minimum,editorFieldsField1.Maximum);
        filterPanelField0.Text=EmployeeUi.Money(booking.AmountDue);filterPanelField1.Text=booking.Booking.Code;editorFieldsField6.Text=booking.Booking.Code;
        saveButton.Enabled=booking.AllowedActions.Contains(BookingAction.RecordDeposit);
    }
    protected virtual void ShowSuccess(string text,string title)=>MessageBox.Show(this,text,title);
    private async Task SaveAsync(){
        if(booking is null)throw new BusinessException("Chưa tải được đơn.");
        if(editorFieldsField3.SelectedIndex==0&&evidence is null)throw new BusinessException("Vui lòng chọn chứng từ chuyển khoản.");
        var result=await services!.Payments.RecordReceiptAsync(new(){BookingId=bookingId,BookingCode=booking.Booking.Code,Amount=editorFieldsField1.Value,
            TransferredAtUtc=DateTime.SpecifyKind(editorFieldsField2.Value.AddHours(-7),DateTimeKind.Utc),
            Method=editorFieldsField3.SelectedIndex==0?PaymentMethod.BankTransfer:PaymentMethod.Cash,PayerName=editorFieldsField4.Text,PayerAccount=editorFieldsField5.Text,
            TransferContent=editorFieldsField6.Text,EvidenceBytes=evidence,EvidenceFileName=evidenceName,RowVer=booking.Booking.RowVer});
        filterPanelField2.Text="Chờ xác minh — "+result.VoucherNo;saveButton.Enabled=false;editorFields.Enabled=false;fileButton.Enabled=false;
        ShowSuccess("Đã ghi phiếu "+result.VoucherNo+". Đơn chờ kế toán xác minh."+(result.Warning is null?"":"\n"+result.Warning),"Ghi nhận thu cọc");
        services.Navigate("F09",bookingId);CloseAfterMutation();
    }
}