using Homestay.DTO.Bookings;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Common;
/// <summary>Hàm hỗ trợ giao diện cho các thao tác vòng đời đơn: kiểm tra có được làm không, hiển thị thông báo, xem tiền hoàn.</summary>
internal static class LifecycleUi
{
    public static bool ReceiptReviewAvailable => true;
    public static bool BookingLifecycleAvailable => true;
    public static bool CancellationAvailable => true;
    public static bool RefundPaymentAvailable => true;
    public const string ReadOnlyNotice="Các thao tác gửi/duyệt hủy và chi hoàn đang được hoàn thiện.";
    public static bool CanWrite(BookingAction action)=>action switch{
        BookingAction.RecordDeposit or BookingAction.CancelUnpaid or BookingAction.Claim or BookingAction.Reassign=>true,
        BookingAction.ConfirmOnBehalf or BookingAction.RejectOnBehalf or BookingAction.CheckIn or BookingAction.CheckOut or BookingAction.MarkNoShow=>BookingLifecycleAvailable,
        BookingAction.CancelHomestayUnavailable or BookingAction.SubmitCancel=>CancellationAvailable,_=>false};
    public static void ShowRefund(IWin32Window owner,BookingRefundPreview preview){
        var c=preview.Calculation;
        MessageBox.Show(owner,"Ngày tính (máy chủ): "+EmployeeUi.Date(preview.CancellationDate)+
            "\nTrước ngày nhận: "+c.DaysBefore+" ngày\nMức hoàn: "+c.RefundPercent+"%"+
            "\nHoàn dự kiến: "+EmployeeUi.Money(c.RefundAmount)+"\nGiữ lại: "+EmployeeUi.Money(c.RetainedAmount)+
            (c.CommissionAmount is decimal commission?"\nHoa hồng: "+EmployeeUi.Money(commission):"")+
            (c.OwnerPayoutAmount is decimal payout?"\nChuyển chủ: "+EmployeeUi.Money(payout):"")+
            "\n\nĐây là số xem trước. Dịch vụ kiểm tra lại khi gửi yêu cầu.","Tiền hoàn dự kiến");
    }
}
