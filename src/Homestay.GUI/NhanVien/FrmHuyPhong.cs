using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Staff;
public partial class FrmCancellation : ScreenForm
{
    private AppServices? services;
    private int bookingId;
    private BookingDetail? booking;
    private BookingRefundPreview? preview;
    private readonly AsyncLoader loader=new();
    public FrmCancellation(){
        InitializeComponent();HelpText="Chỉ hỗ trợ Hủy. Tiền hoàn lấy từ dịch vụ theo chính sách đã lưu và giờ máy chủ. Gửi yêu cầu chưa hủy ngay đơn. Người gửi không tự duyệt; mức hoàn được chốt lại khi gửi.";
        Disposed+=(_,_)=>loader.Dispose();
    }
    public FrmCancellation(AppServices services,int bookingId):this(){
        if(!services.Can(Permission.BookingCancellationRequest))throw new ForbiddenException();
        this.services=services;this.bookingId=bookingId;previewButton.Tag=saveButton.Tag=null;
        pendingNotice.Text="Gửi yêu cầu chưa hủy ngay đơn. Tiền hoàn được chốt lúc gửi; cần Quản lý khác duyệt.";pendingNotice.Visible=true;
        editorFieldsField0.ReadOnly=true;editorFieldsField1.MaxLength=500;saveButton.Enabled=false;
        filterPanelField0.ReadOnly=filterPanelField1.ReadOnly=filterPanelField2.ReadOnly=true;
        filterPanelField1Label.Text="Tiền áp dụng chính sách";
        previewButton.Click+=async(_,_)=>await ErrorHandler.RunAsync(previewButton,LoadAsync);
        saveButton.Click+=async(_,_)=>await RunMutationAsync(saveButton,async()=>{
            if(preview is null)throw new BusinessException("Vui lòng tải lại tiền hoàn dự kiến.");
            if(string.IsNullOrWhiteSpace(editorFieldsField1.Text))throw new BusinessException("Vui lòng nhập lý do hủy.");
            var result=await services.Changes.SubmitCancelAsync(new(bookingId,editorFieldsField1.Text.Trim(),preview.RowVer));
            pendingNotice.Text="Đã gửi yêu cầu hủy. Hoàn chốt: "+EmployeeUi.Money(result.Request.QuotedRefundAmount??0);
            services.Navigate("F09",bookingId);CloseAfterMutation();
        },LoadAsync,()=>LifecycleUi.CancellationAvailable&&booking?.AllowedActions.Contains(BookingAction.SubmitCancel)==true);
        Shown+=async(_,_)=>await ErrorHandler.RunAsync(previewButton,LoadAsync);
    }
    private async Task LoadAsync(){
        booking=null;preview=null;saveButton.Enabled=false;
        await loader.LoadAsync(async ct=>{
        var b=await services!.Bookings.GetAsync(bookingId,ct);var p=await services.Changes.PreviewCancelAsync(bookingId,ct);return (Booking:b,Preview:p);
    },data=>{
        booking=data.Booking;preview=data.Preview;var c=preview.Calculation;
        editorFieldsField0.Text=booking.Booking.Code;
        filterPanelField0.Text=booking.CancellationPolicy.Name+" · Ngày tính "+EmployeeUi.Date(preview.CancellationDate);
        filterPanelField1.Text=EmployeeUi.Money(c.RefundAmount+c.RetainedAmount);
        filterPanelField2.Text=EmployeeUi.Money(c.RefundAmount)+" · "+c.RefundPercent+"% · Trước nhận "+c.DaysBefore+" ngày";
        saveButton.Enabled=LifecycleUi.CancellationAvailable&&booking.AllowedActions.Contains(BookingAction.SubmitCancel);
    });
    }
    protected override bool ProcessCmdKey(ref Message m,Keys k){if(services is not null&&k==Keys.F5){previewButton.PerformClick();return true;}return base.ProcessCmdKey(ref m,k);}
}
