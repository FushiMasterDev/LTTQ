using Homestay.DTO.Payments;
using Homestay.GUI.Common;
using Homestay.GUI.Staff;

namespace Homestay.GUI.Accountant;

/// <summary>Tạo hộp thoại nhập thông tin đã chuyển hoàn tiền (mã giao dịch, thời điểm, chứng từ).</summary>
public static class RefundPaymentEditor
{
    public static FieldsDialog Create(PaymentQueueItem item,DateTime serverNowUtc){
        var now=serverNowUtc.AddHours(7);
        var dialog=new FieldsDialog("Ghi nhận đã chi "+item.VoucherNo,
            new("confirmed","Tôi đã đối chiếu giao dịch chi thực tế, đúng người nhận và số tiền.",FieldKind.Confirmation,false),
            new("reference","Mã giao dịch",MaxLength:100),new("date","Ngày chi",FieldKind.Date,DateOnly.FromDateTime(now)),
            new("time","Giờ chi (giờ Việt Nam)",FieldKind.Time,new TimeOnly(now.Hour,now.Minute)),
            new("attach","Đính kèm chứng từ?",FieldKind.Choice,false,Required:false,Choices:[new(false,"Không đính kèm"),new(true,"Chọn tệp chứng từ")]));
        dialog.SetSummary("Phiếu "+item.VoucherNo+" · Đơn "+item.BookingCode+" · Số tiền: "+EmployeeUi.Money(item.Amount)+" · Người nhận: "+(item.PayerOrPayeeName??item.CustomerName));
        dialog.ValidateValues=values=>values["reference"] is string reference&&reference.Any(c=>c>127||char.IsControl(c))
            ?"Mã giao dịch chỉ nhận ký tự ASCII; vui lòng kiểm tra lại.":null;
        return dialog;
    }
}