using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ đối soát với chủ nhà theo kỳ: lập/lập lại bảng nháp, gửi, mở lại, đánh dấu đã chi và ghi phản hồi của chủ.</summary>
public sealed class SettlementService(Authorizer authorizer,ICurrentSession current,ConnectionFactory factory,
    WriteOperationRunner writes,SettlementRepository repository,DbClock clock,SettingsProvider settings,AuditWriter audit,string machineName)
{
    public Task<IReadOnlyList<SettlementListItem>> ListAsync(int year=0,int month=0,int period=0,CancellationToken ct=default)
    {authorizer.Require(Permission.SettlementView);if(year is not (0) and (<2020 or >2100)||month is <0 or >12||period is <0 or >2)throw new BusinessException("Bộ lọc kỳ đối soát không hợp lệ.");return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>repository.ListAsync(db,year,month,period,token),t),ct);}

    public Task<SettlementDraftResult> CreateOrRecreateDraftAsync(int homestayId,int year,int month,int period,CancellationToken ct=default)
    {
        authorizer.Require(Permission.SettlementCreate);ValidatePeriod(homestayId,year,month,period);
        return writes.ExecuteAsync(async(db,t)=>{
            var session=current.RequireSession();await db.AcquireAppLockAsync($"Settlement:{homestayId}",10000,t).ConfigureAwait(false);
            var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);var today=DateOnly.FromDateTime(now.AddHours(7));
            var periodEnd=period==1?new DateOnly(year,month,15):new DateOnly(year,month,1).AddMonths(1).AddDays(-1);
            if(periodEnd.AddDays(await settings.GetSettlementGraceDaysAsync(t,db).ConfigureAwait(false))>=today)throw new BusinessException("Kỳ chưa hết thời gian chờ đối soát.");
            var rows=await repository.GetPeriodBookingsAsync(db,homestayId,year,month,period,t).ConfigureAwait(false);
            var pending=Unfinalized(rows);
            var lines=CalculateLines(rows);
            var totals=Totals(lines);var head=await repository.GetForPeriodAsync(db,homestayId,year,month,period,true,t).ConfigureAwait(false);
            if(head is null&&lines.Length==0)throw new BusinessException("Kỳ này không có đơn để đối soát.");
            int id;
            if(head is null)id=await repository.InsertAsync(db,homestayId,year,month,period,totals,t).ConfigureAwait(false);
            else
            {
                if(head.Status!=SettlementStatus.Draft)throw new BusinessException("Bảng đã gửi hoặc đã trả, không thể tính lại.");
                if(await repository.UpdateDraftAsync(db,head.Id,head.RowVer,totals,t).ConfigureAwait(false)==0)throw new ConcurrencyException();
                id=head.Id;
            }
            await repository.InsertItemsAsync(db,id,lines,t).ConfigureAwait(false);
            await audit.WriteAsync(db,new(session.UserId,head is null?"Settlement.Create":"Settlement.Recreate","Settlements",id.ToString(CultureInfo.InvariantCulture),head is null?null:new{head.Status,head.PayoutAmount},new{homestayId,year,month,period,Totals=totals,ItemCount=lines.Length,BlockedBookingCodes=pending},null,machineName),t).ConfigureAwait(false);
            var actual=await repository.GetByIdAsync(db,id,false,t).ConfigureAwait(false)??throw new InvalidOperationException();return new SettlementDraftResult(id,homestayId,year,month,period,actual.Status,totals,lines,pending,actual.RowVer){HomestayName=actual.HomestayName,OwnerName=actual.OwnerName,OwnerFeedback=actual.OwnerFeedback,OwnerBankName=actual.OwnerBankName,OwnerBankAccountNo=actual.OwnerBankAccountNo,OwnerBankAccountName=actual.OwnerBankAccountName,History=await repository.GetHistoryAsync(db,id,t).ConfigureAwait(false)};
        },ct);
    }

    public Task<SettlementDraftResult> GetAsync(int id,CancellationToken ct=default)
    {authorizer.Require(Permission.SettlementView);if(id<=0)throw new BusinessException("Mã đối soát không hợp lệ.");return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>ReadResultAsync(db,id,token),t),ct);}

    public Task<SettlementDraftResult> SendAsync(SettlementReasonRequest request,CancellationToken ct=default)=>TransitionAsync(request,SettlementStatus.Draft,SettlementStatus.Sent,"Settlement.Send",null,null,ct);
    public Task<SettlementDraftResult> ReopenAsync(SettlementReasonRequest request,CancellationToken ct=default)=>TransitionAsync(request,SettlementStatus.Sent,SettlementStatus.Draft,"Settlement.Reopen",request.Reason,null,ct);
    public Task<SettlementDraftResult> MarkPaidAsync(RecordSettlementPaidRequest request,CancellationToken ct=default)
    {RequireAccountant(Permission.SettlementManage);ArgumentNullException.ThrowIfNull(request);StaffInputRules.RowVer(request.RowVer);return writes.ExecuteAsync(async(db,t)=>{var s=current.RequireSession();var h=await repository.GetByIdAsync(db,request.SettlementId,true,t).ConfigureAwait(false)??throw new NotFoundException();if(!h.RowVer.AsSpan().SequenceEqual(request.RowVer))throw new ConcurrencyException();if(h.Status!=SettlementStatus.Sent)throw new BusinessException("Chỉ ghi đã trả cho bảng đang Đã gửi.");if(h.PayoutAmount>0&&(string.IsNullOrWhiteSpace(h.OwnerBankName)||string.IsNullOrWhiteSpace(h.OwnerBankAccountNo)||string.IsNullOrWhiteSpace(h.OwnerBankAccountName)))throw new BusinessException("Chủ homestay chưa có đủ thông tin ngân hàng đã xác minh.");var reference=request.BankReference?.Trim();if(h.PayoutAmount>0&&(string.IsNullOrWhiteSpace(reference)||reference.Length>100||reference.Any(char.IsControl)))throw new BusinessException("Nhập mã giao dịch chuyển tiền cho chủ.");var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);if(await repository.UpdateStatusAsync(db,h.Id,request.RowVer,SettlementStatus.Sent,SettlementStatus.Paid,s.UserId,now,reference,null,t).ConfigureAwait(false)==0)throw new ConcurrencyException();await audit.WriteAsync(db,new(s.UserId,"Settlement.Pay","Settlements",h.Id.ToString(CultureInfo.InvariantCulture),new{Status="Sent"},new{Status="Paid",h.PayoutAmount,BankReference=reference},null,machineName),t).ConfigureAwait(false);return await ReadResultAsync(db,h.Id,t).ConfigureAwait(false);},ct);}
    public Task<SettlementDraftResult> RecordOwnerFeedbackAsync(SettlementFeedbackRequest request,CancellationToken ct=default)
    {RequireAccountant(Permission.SettlementManage);ArgumentNullException.ThrowIfNull(request);StaffInputRules.RowVer(request.RowVer);var text=BookingService.RequiredLifecycleNote(request.Feedback);return writes.ExecuteAsync(async(db,t)=>{var s=current.RequireSession();var h=await repository.GetByIdAsync(db,request.SettlementId,true,t).ConfigureAwait(false)??throw new NotFoundException();if(!h.RowVer.AsSpan().SequenceEqual(request.RowVer))throw new ConcurrencyException();if(await repository.UpdateFeedbackAsync(db,h.Id,h.RowVer,text,t).ConfigureAwait(false)==0)throw new ConcurrencyException();await audit.WriteAsync(db,new(s.UserId,"Settlement.OwnerFeedback","Settlements",h.Id.ToString(CultureInfo.InvariantCulture),new{h.OwnerFeedback},new{OwnerFeedback=text},text,machineName),t).ConfigureAwait(false);return await ReadResultAsync(db,h.Id,t).ConfigureAwait(false);},ct);}

    private Task<SettlementDraftResult> TransitionAsync(SettlementReasonRequest request,SettlementStatus from,SettlementStatus to,string action,string? reason,string? bankRef,CancellationToken ct)
    {RequireAccountant(Permission.SettlementManage);ArgumentNullException.ThrowIfNull(request);StaffInputRules.RowVer(request.RowVer);string? note=null;if(to==SettlementStatus.Draft)note=BookingService.RequiredLifecycleNote(reason);return writes.ExecuteAsync(async(db,t)=>{var s=current.RequireSession();var h=await repository.GetByIdAsync(db,request.SettlementId,true,t).ConfigureAwait(false)??throw new NotFoundException();if(!h.RowVer.AsSpan().SequenceEqual(request.RowVer))throw new ConcurrencyException();if(h.Status!=from)throw new BusinessException("Trạng thái đối soát đã thay đổi.");
        if(to==SettlementStatus.Sent){var rows=await repository.GetPeriodBookingsAsync(db,h.HomestayId,h.Year,h.Month,h.PeriodNo,t).ConfigureAwait(false);var blocked=Unfinalized(rows);if(blocked.Length>0)throw new BusinessException("Còn đơn chưa chốt trong kỳ: "+string.Join(", ",blocked));var lines=CalculateLines(rows);var stored=await repository.GetItemsAsync(db,h.Id,t).ConfigureAwait(false);if(!lines.OrderBy(x=>x.BookingId).SequenceEqual(stored.OrderBy(x=>x.BookingId)))throw new BusinessException("Số liệu kỳ đã thay đổi, hãy lập lại bảng nháp trước khi gửi.");}
        var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);if(await repository.UpdateStatusAsync(db,h.Id,h.RowVer,from,to,s.UserId,now,bankRef,null,t).ConfigureAwait(false)==0)throw new ConcurrencyException();await audit.WriteAsync(db,new(s.UserId,action,"Settlements",h.Id.ToString(CultureInfo.InvariantCulture),new{Status=from.ToString()},new{Status=to.ToString()},note,machineName),t).ConfigureAwait(false);return await ReadResultAsync(db,h.Id,t).ConfigureAwait(false);},ct);}
    private async Task<SettlementDraftResult> ReadResultAsync(DbSession db,int id,CancellationToken ct){var h=await repository.GetByIdAsync(db,id,false,ct).ConfigureAwait(false)??throw new NotFoundException();var rows=await repository.GetPeriodBookingsAsync(db,h.HomestayId,h.Year,h.Month,h.PeriodNo,ct).ConfigureAwait(false);var blocked=Unfinalized(rows);return new SettlementDraftResult(h.Id,h.HomestayId,h.Year,h.Month,h.PeriodNo,h.Status,new(h.TotalRoomAmount,h.TotalRetained,h.TotalCommission,h.TotalDiscount,h.PayoutAmount),await repository.GetItemsAsync(db,id,ct).ConfigureAwait(false),blocked,h.RowVer){HomestayName=h.HomestayName,OwnerName=h.OwnerName,OwnerFeedback=h.OwnerFeedback,OwnerBankName=h.OwnerBankName,OwnerBankAccountNo=h.OwnerBankAccountNo,OwnerBankAccountName=h.OwnerBankAccountName,History=await repository.GetHistoryAsync(db,id,ct).ConfigureAwait(false)};}
    private static SettlementLine[] CalculateLines(IReadOnlyList<SettlementBookingRow> rows)=>rows.Select(x=>SettlementCalculator.Calculate(new(x.Id,x.Code,x.Status,x.CheckOut,x.Replaced,x.TotalAmount,x.AmountDue,x.DepositAmount,x.RefundAmount,x.ChangeFeeAmount,x.DiscountAmount,x.CommissionRate,x.HasVerifiedDeposit))).Where(x=>x is not null).Cast<SettlementLine>().ToArray();
    private static string[] Unfinalized(IReadOnlyList<SettlementBookingRow> rows)=>rows.Where(x=>!x.Replaced&&x.Status is (BookingStatus.AwaitingPayment or BookingStatus.PaymentSubmitted or BookingStatus.Deposited or BookingStatus.Confirmed or BookingStatus.CheckedIn)).Select(x=>x.Code).ToArray();
    private void RequireAccountant(Permission permission){authorizer.Require(permission);if(current.RequireSession().Position!=Position.Accountant)throw new ForbiddenException("Thao tác ghi đối soát chỉ dành cho Kế toán.");}
    private static SettlementTotals Totals(IReadOnlyList<SettlementLine> rows)=>new(rows.Sum(x=>x.RoomAmount),rows.Sum(x=>x.RetainedAmount),rows.Sum(x=>x.CommissionAmount),rows.Sum(x=>x.DiscountAmount),rows.Sum(x=>x.PayoutAmount));
    private static void ValidatePeriod(int home,int year,int month,int period){if(home<=0||year is <2020 or >2100||month is <1 or >12||period is <1 or >2)throw new BusinessException("Kỳ đối soát không hợp lệ.");}
}
