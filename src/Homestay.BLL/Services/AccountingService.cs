using System.Globalization;
using System.Text.RegularExpressions;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.BLL.Services;

public sealed class AccountingService(Authorizer authorizer, ICurrentSession current, ConnectionFactory factory,
    WriteOperationRunner writes, FinanceRepository finance, BookingRepository bookings,
    BookingService bookingService, DbClock clock, SettingsProvider settings, AuditWriter audit, string machineName)
{
    public Task<IReadOnlyList<BankTransactionItem>> ListBankTransactionsAsync(DateTime fromUtc, DateTime toUtc, MatchStatus? status, string? search, CancellationToken ct=default)
    {
        authorizer.Require(Permission.BankStatementManage); ValidateRange(fromUtc,toUtc);
        return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>finance.ListBankTransactionsAsync(db,fromUtc,toUtc,status?.ToString(),search?.Trim()??"",token),t),ct);
    }

    public Task<int> RecordBankTransactionAsync(RecordBankTransactionRequest request,CancellationToken ct=default)
    {
        RequireAccountant(Permission.BankStatementManage); ArgumentNullException.ThrowIfNull(request);
        var x=Normalize(request);
        return writes.ExecuteAsync(async(db,t)=>{
            var session=current.RequireSession();var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);
            var id=await finance.InsertBankTransactionAsync(db,x,session.UserId,now,t).ConfigureAwait(false);
            await audit.WriteAsync(db,new(session.UserId,"BankTransaction.Record","BankTransactions",id.ToString(CultureInfo.InvariantCulture),null,
                new{x.AccountNumber,x.BankReference,x.Amount,x.TransferContent,x.SenderName,SenderAccount=ReceiptRules.MaskAccount(x.SenderAccount),x.TransactionTimeUtc,MatchStatus="Unmatched"},null,machineName),t).ConfigureAwait(false);
            return id;
        },ct);
    }

    public Task<IReadOnlyList<BankTransactionItem>> AutoMatchAsync(DateTime fromUtc,DateTime toUtc,CancellationToken ct=default)
    {
        RequireAccountant(Permission.BankStatementManage);ValidateRange(fromUtc,toUtc);
        return BusinessErrorTranslator.ExecuteAsync(async t=>{
            var rows=await factory.ReadAsync((db,token)=>finance.ListBankTransactionsAsync(db,fromUtc,toUtc,"Unmatched","",token),t).ConfigureAwait(false);
            foreach(var row in rows) { var code=ExtractBookingCode(row.TransferContent);if(code is null)continue;
                try { var booking=await bookingService.GetByCodeAsync(code,t).ConfigureAwait(false);await MatchBankTransactionAsync(new(row.Id,booking.Booking.Id,row.RowVer),true,t).ConfigureAwait(false); }
                catch(BusinessException) { /* keep ambiguous/late/underpaid transactions available for accountant review */ }
            }
            return await factory.ReadAsync((db,token)=>finance.ListBankTransactionsAsync(db,fromUtc,toUtc,null,"",token),t).ConfigureAwait(false);
        },ct);
    }

    public Task<BankTransactionItem> MatchBankTransactionAsync(MatchBankTransactionRequest request,CancellationToken ct=default)
        => MatchBankTransactionAsync(request,false,ct);

    private Task<BankTransactionItem> MatchBankTransactionAsync(MatchBankTransactionRequest request,bool automatic,CancellationToken ct)
    {
        RequireAccountant(Permission.BankStatementManage);ArgumentNullException.ThrowIfNull(request);StaffInputRules.RowVer(request.RowVer);
        return writes.ExecuteAsync(async(db,t)=>{
            var session=current.RequireSession();var tx=await finance.GetBankTransactionForUpdateAsync(db,request.TransactionId,request.RowVer,t).ConfigureAwait(false)??throw new ConcurrencyException();
            if(tx.MatchStatus is not (MatchStatus.Unmatched or MatchStatus.Underpaid or MatchStatus.LatePayment))throw new BusinessException("Giao dịch đã được xử lý hoặc không còn ở trạng thái chờ.");
            var scope=new DataScope(current).BookingFilter();
            var booking=await bookings.GetForUpdateAsync(db,request.BookingId,scope.Condition,scope.AddParametersTo,t).ConfigureAwait(false)??throw new NotFoundException();
            var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);
            var due=await finance.GetAmountDueAsync(db,booking.Id,t).ConfigureAwait(false);
            if(tx.Amount<due){await finance.SetBankStatusAsync(db,tx.Id,tx.RowVer,MatchStatus.Underpaid,booking.Id,null,null,null,null,null,t).ConfigureAwait(false);return tx with{MatchStatus=MatchStatus.Underpaid,MatchedBookingId=booking.Id,BookingCode=booking.Code};}
            if(booking.Status is BookingStatus.Expired or BookingStatus.Cancelled or BookingStatus.Rejected){await finance.SetBankStatusAsync(db,tx.Id,tx.RowVer,MatchStatus.LatePayment,booking.Id,null,null,null,null,null,t).ConfigureAwait(false);return tx with{MatchStatus=MatchStatus.LatePayment,MatchedBookingId=booking.Id,BookingCode=booking.Code};}
            if(booking.Status is not (BookingStatus.AwaitingPayment or BookingStatus.PaymentSubmitted))throw new BusinessException("Chỉ ghép tiền vào đơn đang chờ thanh toán hoặc chờ xác minh.");
            if(booking.Status==BookingStatus.AwaitingPayment && booking.HoldExpiresAtUtc<=now)throw new BusinessException("Đơn đã hết thời gian giữ phòng. Hãy xử lý khoản chuyển muộn.");
            var pending=await finance.GetExistingDepositAsync(db,booking.Id,t).ConfigureAwait(false);
            if(pending is null && await bookings.HasActiveDepositAsync(db,booking.Id,t))throw new BusinessException("Đơn đã có khoản cọc được xác minh hoặc đang chờ xử lý.");
            var status=automatic?MatchStatus.Matched:MatchStatus.ManuallyMatched;
            var note=tx.Amount>due?$"Chuyển dư {tx.Amount-due:0} VND; xử lý hoàn khi khách yêu cầu.":null;
            if(await finance.SetBankStatusAsync(db,tx.Id,tx.RowVer,status,booking.Id,session.UserId,now,note,null,null,t).ConfigureAwait(false)==0)throw new ConcurrencyException();
            if(pending is { } paymentId){if(await finance.AttachBankAndVerifyPaymentAsync(db,paymentId,tx,session.UserId,now,t).ConfigureAwait(false)==0)throw new ConcurrencyException();}
            else {var voucher=ReceiptRules.VoucherNo(now,await bookings.NextSequenceAsync(db,ReceiptRules.CounterKey(now),t).ConfigureAwait(false));if(await finance.InsertVerifiedDepositAsync(db,tx,booking.Id,voucher,session.UserId,now,t).ConfigureAwait(false)==0)throw new InvalidOperationException("Không tạo được phiếu thu sao kê.");}
            var target=BookingStatus.Deposited;var actor=BookingTransitionActor.Accountant;
            await new BookingStatusWriter(bookings,audit).ChangeStatusAsync(db,booking.Id,booking.Status,target,booking.RowVer,actor,now,
                new(session.UserId,automatic?"BankTransaction.AutoMatch":"BankTransaction.ManualMatch","BankTransactions",tx.Id.ToString(CultureInfo.InvariantCulture),
                    new{Status=tx.MatchStatus.ToString()},new{Status=status.ToString(),BookingId=booking.Id,BookingCode=booking.Code,Amount=tx.Amount},null,machineName),
                new(){ConfirmDeadlineAtUtc=now.AddMinutes(await settings.GetBookingConfirmMinutesAsync(t,db).ConfigureAwait(false))},t).ConfigureAwait(false);
            if(tx.Amount>due)await audit.WriteAsync(db,new(session.UserId,"BankTransaction.Excess","BankTransactions",tx.Id.ToString(CultureInfo.InvariantCulture),null,new{Excess=tx.Amount-due},note,machineName),t).ConfigureAwait(false);
            return tx with{MatchStatus=status,MatchedBookingId=booking.Id,BookingCode=booking.Code,Note=note};
        },ct);
    }

    public Task<BankTransactionItem> IgnoreBankTransactionAsync(ProcessBankTransactionRequest request,CancellationToken ct=default)
        => ProcessBankStatusAsync(request,MatchStatus.Ignored,ct);
    private Task<BankTransactionItem> ProcessBankStatusAsync(ProcessBankTransactionRequest request,MatchStatus status,CancellationToken ct)
    {
        RequireAccountant(Permission.BankStatementManage);StaffInputRules.RowVer(request.RowVer);var note=BookingService.RequiredLifecycleNote(request.Note);
        return writes.ExecuteAsync(async(db,t)=>{var session=current.RequireSession();var tx=await finance.GetBankTransactionForUpdateAsync(db,request.TransactionId,request.RowVer,t).ConfigureAwait(false)??throw new ConcurrencyException();
            if(tx.MatchStatus!=MatchStatus.Unmatched)throw new BusinessException("Chỉ được bỏ qua giao dịch chưa khớp.");var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);
            if(await finance.SetBankStatusAsync(db,tx.Id,tx.RowVer,status,null,session.UserId,now,note,null,null,t).ConfigureAwait(false)==0)throw new ConcurrencyException();
            await audit.WriteAsync(db,new(session.UserId,"BankTransaction.Ignore","BankTransactions",tx.Id.ToString(CultureInfo.InvariantCulture),new{Status=tx.MatchStatus.ToString()},new{Status=status.ToString()},note,machineName),t).ConfigureAwait(false);return tx with{MatchStatus=status,Note=note};},ct);
    }

    public Task<BankTransactionItem> RecordUnmatchedRefundAsync(RefundBankTransactionRequest request,CancellationToken ct=default)
    {
        authorizer.Require(Permission.UnmatchedPaymentRefund);StaffInputRules.RowVer(request.RowVer);
        if(string.IsNullOrWhiteSpace(request.RefundBankRef)||request.RefundBankRef.Trim().Length>100||request.RefundBankRef.Any(char.IsControl)||request.RefundedAtUtc.Kind!=DateTimeKind.Utc)throw new BusinessException("Thông tin hoàn giao dịch không hợp lệ.");
        return writes.ExecuteAsync(async(db,t)=>{var session=current.RequireSession();var tx=await finance.GetBankTransactionForUpdateAsync(db,request.TransactionId,request.RowVer,t).ConfigureAwait(false)??throw new ConcurrencyException();if(tx.MatchStatus is not (MatchStatus.Unmatched or MatchStatus.LatePayment))throw new BusinessException("Chỉ được hoàn giao dịch chưa khớp hoặc chuyển muộn.");var now=await clock.GetUtcNowAsync(db,t).ConfigureAwait(false);if(request.RefundedAtUtc>now.AddMinutes(5))throw new BusinessException("Thời điểm hoàn không thể ở tương lai.");var note=string.IsNullOrWhiteSpace(request.Note)?tx.Note:request.Note.Trim();
            if(await finance.SetBankStatusAsync(db,tx.Id,tx.RowVer,MatchStatus.Refunded,tx.MatchedBookingId,session.UserId,now,note,request.RefundBankRef.Trim(),request.RefundedAtUtc,t).ConfigureAwait(false)==0)throw new ConcurrencyException();
            await audit.WriteAsync(db,new(session.UserId,"BankTransaction.Refund","BankTransactions",tx.Id.ToString(CultureInfo.InvariantCulture),new{Status=tx.MatchStatus.ToString()},new{Status="Refunded",request.RefundBankRef,request.RefundedAtUtc},note,machineName),t).ConfigureAwait(false);return tx with{MatchStatus=MatchStatus.Refunded,Note=note};},ct);
    }

    public Task<IReadOnlyList<CashBookItem>> ListCashBookAsync(DateTime fromUtc,DateTime toUtc,CancellationToken ct=default){authorizer.Require(Permission.CashBookManage);ValidateRange(fromUtc,toUtc);return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>finance.ListCashBookAsync(db,fromUtc,toUtc,token),t),ct);}
    public Task<IReadOnlyList<PayableItem>> ListPayablesAsync(CancellationToken ct=default){authorizer.Require(Permission.CashBookManage);return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>finance.ListPayablesAsync(db,token),t),ct);}
    public Task<AccountantDashboard> GetAccountantDashboardAsync(CancellationToken ct=default){authorizer.Require(Permission.PaymentVerify);return BusinessErrorTranslator.ExecuteAsync(t=>factory.ReadAsync((db,token)=>finance.DashboardAsync(db,token),t),ct);}

    private static RecordBankTransactionRequest Normalize(RecordBankTransactionRequest x)
    {
        string Clean(string? v,int max,string label,bool required=true){var s=v?.Trim()??"";if((required&&s.Length==0)||s.Length>max||s.Any(char.IsControl))throw new BusinessException($"{label} không hợp lệ.");return s;}
        if(x.Amount<=0||x.Amount!=decimal.Truncate(x.Amount)||x.Amount>999_999_999_999_999_999m||x.TransactionTimeUtc.Kind!=DateTimeKind.Utc||x.TransactionTimeUtc==default)throw new BusinessException("Số tiền hoặc thời gian giao dịch không hợp lệ.");
        return x with{AccountNumber=Clean(x.AccountNumber,30,"Số tài khoản"),BankReference=Clean(x.BankReference,100,"Mã giao dịch"),TransferContent=Clean(x.TransferContent,500,"Nội dung",false),SenderName=Clean(x.SenderName,100,"Tên người chuyển",false),SenderAccount=Clean(x.SenderAccount,30,"Tài khoản người chuyển",false)};
    }
    private static string? ExtractBookingCode(string? content){if(string.IsNullOrWhiteSpace(content))return null;var match=Regex.Match(content.ToUpperInvariant(),@"FJ\s*[- ]?\s*(\d{6})\s*[- ]?\s*(\d{3})",RegexOptions.CultureInvariant);return match.Success?$"FJ{match.Groups[1].Value}-{match.Groups[2].Value}":null;}
    private static void ValidateRange(DateTime from,DateTime to){if(from.Kind!=DateTimeKind.Utc||to.Kind!=DateTimeKind.Utc||to<=from||to-from>TimeSpan.FromDays(366))throw new BusinessException("Khoảng thời gian phải là UTC, tối đa 366 ngày.");}
    private void RequireAccountant(Permission permission){authorizer.Require(permission);if(current.RequireSession().Position!=Position.Accountant)throw new ForbiddenException("Thao tác ghi sao kê chỉ dành cho Kế toán.");}
}
