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
    public FrmReceipt(){InitializeComponent();HelpText="Chuyển khoản bắt buộc có ảnh chứng từ. Số tiền và thời điểm khách chuyển được kiểm tra ở máy chủ. Phiếu lưu ở trạng thái Chờ xác minh."; }
    public FrmReceipt(AppServices services,int bookingId):this(){
        if(!services.Can(Permission.PaymentRecord))throw new ForbiddenException();
        this.services=services;this.bookingId=bookingId;EmployeeUi.Activate(pendingNotice,fileButton,saveButton);saveButton.Enabled=false;
        editorFieldsField1.Minimum=1;editorFieldsField1.Maximum=999999999999999999m;editorFieldsField1.DecimalPlaces=0;
        editorFieldsField2.CustomFormat="dd/MM/yyyy HH:mm";editorFieldsField2.Value=DateTime.UtcNow.AddHours(7);
        editorFieldsField2Label.Text="Khách chuyển / nhận tiền lúc (giờ Việt Nam)";
        editorFieldsField3.SelectedIndex=0;
        fileButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(fileButton,async()=>{
            using var d=new OpenFileDialog{Filter="Ảnh chứng từ (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"};
            if(d.ShowDialog(this)!=DialogResult.OK)return;
            if(new FileInfo(d.FileName).Length>Homestay.BLL.Rules.ReceiptRules.MaxEvidenceBytes)throw new BusinessException("Ảnh chứng từ tối đa 5 MB.");
            evidence=await File.ReadAllBytesAsync(d.FileName);evidenceName=Path.GetFileName(d.FileName);editorFieldsField7.Text=evidenceName;
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
        if(editorFieldsField3.SelectedIndex==0&&evidence is null)throw new BusinessException("Vui lòng chọn ảnh chứng từ chuyển khoản.");
        var result=await services!.Payments.RecordReceiptAsync(new(){BookingId=bookingId,BookingCode=booking.Booking.Code,Amount=editorFieldsField1.Value,
            TransferredAtUtc=DateTime.SpecifyKind(editorFieldsField2.Value.AddHours(-7),DateTimeKind.Utc),
            Method=editorFieldsField3.SelectedIndex==0?PaymentMethod.BankTransfer:PaymentMethod.Cash,PayerName=editorFieldsField4.Text,PayerAccount=editorFieldsField5.Text,
            TransferContent=editorFieldsField6.Text,EvidenceBytes=evidence,EvidenceFileName=evidenceName,RowVer=booking.Booking.RowVer});
        filterPanelField2.Text="Chờ xác minh — "+result.VoucherNo;saveButton.Enabled=false;editorFields.Enabled=false;fileButton.Enabled=false;
        ShowSuccess("Đã ghi phiếu "+result.VoucherNo+". Đơn chờ kế toán xác minh."+(result.Warning is null?"":"\n"+result.Warning),"Ghi nhận thu cọc");
        services.Navigate("F09",bookingId);CloseAfterMutation();
    }
}