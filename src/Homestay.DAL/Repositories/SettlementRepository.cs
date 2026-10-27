using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.DAL.Repositories;

public sealed record SettlementBookingRow(int Id,string Code,BookingStatus Status,DateOnly CheckOut,bool Replaced,
    decimal TotalAmount,decimal AmountDue,decimal DepositAmount,decimal? RefundAmount,decimal ChangeFeeAmount,
    decimal DiscountAmount,decimal CommissionRate,bool HasVerifiedDeposit);
public sealed record SettlementHead(int Id,int HomestayId,string HomestayName,string OwnerName,int Year,int Month,int PeriodNo,
    SettlementStatus Status,decimal TotalRoomAmount,decimal TotalRetained,decimal TotalCommission,decimal TotalDiscount,
    decimal PayoutAmount,DateTime? SentAt,DateTime? PaidAt,string? PaidBankRef,string? OwnerFeedback,string? OwnerBankName,
    string? OwnerBankAccountNo,string? OwnerBankAccountName,byte[] RowVer);

public sealed class SettlementRepository
{
    private const string HeadSelect = """
        SELECT s.Id,s.HomestayId,h.Name HomestayName,o.FullName OwnerName,o.BankName OwnerBankName,
            o.BankAccountNo OwnerBankAccountNo,o.BankAccountName OwnerBankAccountName,s.[Year],s.[Month],
            s.PeriodNo,s.Status,s.TotalRoomAmount,s.TotalRetained,s.TotalCommission,s.TotalDiscount,
            s.PayoutAmount,s.SentAt,s.PaidAt,s.PaidBankRef,s.OwnerFeedback,s.RowVer
        FROM dbo.Settlements s
        """;

    public async Task<IReadOnlyList<SettlementBookingRow>> GetPeriodBookingsAsync(DbSession db,int homestay,int year,int month,int period,CancellationToken ct)
    {
        var start=new DateOnly(year,month,period==1?1:16);var end=period==1?new DateOnly(year,month,16):new DateOnly(year,month,1).AddMonths(1);
        using var c=db.CreateCommand("""
            SELECT b.Id,b.Code,b.Status,b.CheckOut,CAST(CASE WHEN b.ReplacedByBookingId IS NULL THEN 0 ELSE 1 END AS bit) Replaced,
              b.TotalAmount,b.DepositAmount-b.DiscountAmount AmountDue,b.DepositAmount,b.RefundAmount,b.ChangeFeeAmount,b.DiscountAmount,b.CommissionRate,
              CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Direction='In' AND p.Type='Deposit' AND p.Status='Verified') THEN 1 ELSE 0 END AS bit) HasVerifiedDeposit
            FROM dbo.Bookings b WHERE b.HomestayId=@home AND b.CheckOut>=@start AND b.CheckOut<@end
            ORDER BY b.CheckOut,b.Code,b.Id;
            """);c.Parameters.AddInt32("@home",homestay);c.Parameters.AddDate("@start",start);c.Parameters.AddDate("@end",end);
        return await db.QueryAsync(c,r=>new SettlementBookingRow(r.GetInt32("Id"),r.GetString("Code"),r.GetEnum<BookingStatus>("Status"),r.GetDateOnly("CheckOut"),r.GetBoolean("Replaced"),r.GetDecimal("TotalAmount"),r.GetDecimal("AmountDue"),r.GetDecimal("DepositAmount"),r.GetNullableDecimal("RefundAmount"),r.GetDecimal("ChangeFeeAmount"),r.GetDecimal("DiscountAmount"),r.GetDecimal("CommissionRate"),r.GetBoolean("HasVerifiedDeposit")),ct).ConfigureAwait(false);
    }
    public async Task<SettlementHead?> GetForPeriodAsync(DbSession db,int home,int year,int month,int period,bool update,CancellationToken ct)
    { using var c=db.CreateCommand($"{HeadSelect} {(update?"WITH (UPDLOCK)":"")} JOIN dbo.Homestays h ON h.Id=s.HomestayId JOIN dbo.Owners o ON o.Id=h.OwnerId WHERE s.HomestayId=@home AND s.[Year]=@year AND s.[Month]=@month AND s.PeriodNo=@period;");c.Parameters.AddInt32("@home",home);c.Parameters.AddInt32("@year",year);c.Parameters.AddInt32("@month",month);c.Parameters.AddInt32("@period",period);return (await db.QueryAsync(c,Map,ct).ConfigureAwait(false)).SingleOrDefault(); }
    public async Task<SettlementHead?> GetByIdAsync(DbSession db,int id,bool update,CancellationToken ct)
    { using var c=db.CreateCommand($"{HeadSelect} {(update?"WITH (UPDLOCK)":"")} JOIN dbo.Homestays h ON h.Id=s.HomestayId JOIN dbo.Owners o ON o.Id=h.OwnerId WHERE s.Id=@id;");c.Parameters.AddInt32("@id",id);return (await db.QueryAsync(c,Map,ct).ConfigureAwait(false)).SingleOrDefault(); }
    public async Task<IReadOnlyList<SettlementListItem>> ListAsync(DbSession db,int year,int month,int period,CancellationToken ct)
    { using var c=db.CreateCommand($"{HeadSelect} JOIN dbo.Homestays h ON h.Id=s.HomestayId JOIN dbo.Owners o ON o.Id=h.OwnerId WHERE (@year=0 OR s.[Year]=@year) AND (@month=0 OR s.[Month]=@month) AND (@period=0 OR s.PeriodNo=@period) ORDER BY s.[Year] DESC,s.[Month] DESC,s.PeriodNo DESC,h.Name,s.Id DESC;");c.Parameters.AddInt32("@year",year);c.Parameters.AddInt32("@month",month);c.Parameters.AddInt32("@period",period);return await db.QueryAsync(c,r=>{var h=Map(r);return ToItem(h);},ct).ConfigureAwait(false); }
    public async Task<IReadOnlyList<SettlementLine>> GetItemsAsync(DbSession db,int id,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT i.BookingId,b.Code BookingCode,b.CheckOut,i.ItemType,i.RoomAmount,i.DepositAmount,i.RefundAmount,i.RetainedAmount,i.CommissionAmount,i.DiscountAmount,i.PayoutAmount,i.CommissionRate FROM dbo.SettlementItems i JOIN dbo.Bookings b ON b.Id=i.BookingId WHERE i.SettlementId=@id ORDER BY b.CheckOut,b.Code;");c.Parameters.AddInt32("@id",id);return await db.QueryAsync(c,r=>new SettlementLine(r.GetInt32("BookingId"),r.GetString("BookingCode"),r.GetEnum<SettlementItemType>("ItemType"),r.GetDecimal("RoomAmount"),r.GetDecimal("DepositAmount"),r.GetDecimal("RefundAmount"),r.GetDecimal("RetainedAmount"),r.GetDecimal("CommissionAmount"),r.GetDecimal("DiscountAmount"),r.GetDecimal("PayoutAmount"),r.GetDecimal("CommissionRate")){CheckOut=r.GetDateOnly("CheckOut")},ct).ConfigureAwait(false); }
    public async Task<IReadOnlyList<SettlementAuditItem>> GetHistoryAsync(DbSession db,int id,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT a.CreatedAt,a.ActionCode,COALESCE(e.FullName,u.Username) ActorName,a.Note FROM dbo.AuditLogs a LEFT JOIN dbo.Users u ON u.Id=a.UserId LEFT JOIN dbo.Employees e ON e.UserId=u.Id WHERE a.TableName='Settlements' AND a.RecordId=@id ORDER BY a.CreatedAt,a.Id;");c.Parameters.AddVarChar("@id",50,id.ToString(System.Globalization.CultureInfo.InvariantCulture));return await db.QueryAsync(c,r=>new SettlementAuditItem(r.GetUtcDateTime("CreatedAt"),r.GetString("ActionCode"),r.GetNullableString("ActorName"),r.GetNullableString("Note")),ct).ConfigureAwait(false); }
    public async Task<int> InsertAsync(UnitOfWork db,int home,int year,int month,int period,SettlementTotals t,CancellationToken ct)
    { using var c=db.CreateCommand("INSERT dbo.Settlements(HomestayId,[Year],[Month],PeriodNo,TotalRoomAmount,TotalRetained,TotalCommission,TotalDiscount,PayoutAmount,Status) VALUES(@home,@year,@month,@period,@room,@retained,@commission,@discount,@payout,'Draft');SELECT CONVERT(int,SCOPE_IDENTITY());");AddTotals(c,home,year,month,period,t);return (int)(await db.ScalarAsync(c,ct).ConfigureAwait(false))!; }
    public async Task<int> UpdateDraftAsync(UnitOfWork db,int id,byte[] version,SettlementTotals t,CancellationToken ct)
    {
        using var c=db.CreateCommand("UPDATE dbo.Settlements SET TotalRoomAmount=@room,TotalRetained=@retained,TotalCommission=@commission,TotalDiscount=@discount,PayoutAmount=@payout,UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND RowVer=@version AND Status='Draft';");
        c.Parameters.AddInt32("@id",id);c.Parameters.AddRowVersion("@version",version);AddTotals(c,null,null,null,null,t);
        var updated=await db.ExecuteAsync(c,ct).ConfigureAwait(false);
        if(updated==0)return 0;
        using var del=db.CreateCommand("DELETE dbo.SettlementItems WHERE SettlementId=@id AND EXISTS(SELECT 1 FROM dbo.Settlements WHERE Id=@id AND Status='Draft');");
        del.Parameters.AddInt32("@id",id);
        await db.ExecuteAsync(del,ct).ConfigureAwait(false);
        return updated;
    }
    private static void AddTotals(Microsoft.Data.SqlClient.SqlCommand c,int? home,int? year,int? month,int? period,SettlementTotals t){if(home.HasValue)c.Parameters.AddInt32("@home",home.Value);if(year.HasValue)c.Parameters.AddInt32("@year",year.Value);if(month.HasValue)c.Parameters.AddInt32("@month",month.Value);if(period.HasValue)c.Parameters.AddInt32("@period",period.Value);c.Parameters.AddMoney("@room",t.TotalRoomAmount);c.Parameters.AddMoney("@retained",t.TotalRetained);c.Parameters.AddMoney("@commission",t.TotalCommission);c.Parameters.AddMoney("@discount",t.TotalDiscount);c.Parameters.AddMoney("@payout",t.PayoutAmount);}
    public async Task InsertItemsAsync(UnitOfWork db,int id,IReadOnlyList<SettlementLine> items,CancellationToken ct)
    { foreach(var x in items){using var c=db.CreateCommand("INSERT dbo.SettlementItems(SettlementId,BookingId,ItemType,RoomAmount,DepositAmount,RefundAmount,RetainedAmount,CommissionAmount,DiscountAmount,PayoutAmount,CommissionRate) VALUES(@settlement,@booking,@type,@room,@deposit,@refund,@retained,@commission,@discount,@payout,@rate);");c.Parameters.AddInt32("@settlement",id);c.Parameters.AddInt32("@booking",x.BookingId);c.Parameters.AddEnum("@type",20,(SettlementItemType?)x.ItemType);c.Parameters.AddMoney("@room",x.RoomAmount);c.Parameters.AddMoney("@deposit",x.DepositAmount);c.Parameters.AddMoney("@refund",x.RefundAmount);c.Parameters.AddMoney("@retained",x.RetainedAmount);c.Parameters.AddMoney("@commission",x.CommissionAmount);c.Parameters.AddMoney("@discount",x.DiscountAmount);c.Parameters.AddMoney("@payout",x.PayoutAmount);c.Parameters.AddRate("@rate",x.CommissionRate);await db.ExecuteAsync(c,ct).ConfigureAwait(false);} }
    public async Task<int> UpdateStatusAsync(UnitOfWork db,int id,byte[] version,SettlementStatus from,SettlementStatus to,int user,DateTime now,string? bankRef,string? feedback,CancellationToken ct)
    { using var c=db.CreateCommand("UPDATE dbo.Settlements SET Status=@to,SentAt=CASE WHEN @to='Sent' THEN @now ELSE SentAt END,SentByUserId=CASE WHEN @to='Sent' THEN @user ELSE SentByUserId END,PaidAt=CASE WHEN @to='Paid' THEN @now ELSE PaidAt END,PaidByUserId=CASE WHEN @to='Paid' THEN @user ELSE PaidByUserId END,PaidBankRef=CASE WHEN @to='Paid' THEN @ref ELSE PaidBankRef END,OwnerFeedback=COALESCE(@feedback,OwnerFeedback),UpdatedAt=@now WHERE Id=@id AND RowVer=@version AND Status=@from;");c.Parameters.AddInt32("@id",id);c.Parameters.AddRowVersion("@version",version);c.Parameters.AddEnum("@from",20,(SettlementStatus?)from);c.Parameters.AddEnum("@to",20,(SettlementStatus?)to);c.Parameters.AddInt32("@user",user);c.Parameters.AddUtcDateTime("@now",now);c.Parameters.AddVarChar("@ref",100,bankRef);c.Parameters.AddNVarChar("@feedback",500,feedback);return await db.ExecuteAsync(c,ct).ConfigureAwait(false); }
    public async Task<int> UpdateFeedbackAsync(UnitOfWork db,int id,byte[] version,string feedback,CancellationToken ct)
    { using var c=db.CreateCommand("UPDATE dbo.Settlements SET OwnerFeedback=@feedback,UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND RowVer=@version;");c.Parameters.AddInt32("@id",id);c.Parameters.AddRowVersion("@version",version);c.Parameters.AddNVarChar("@feedback",500,feedback);return await db.ExecuteAsync(c,ct).ConfigureAwait(false); }
    private static SettlementHead Map(Microsoft.Data.SqlClient.SqlDataReader r)=>new(r.GetInt32("Id"),r.GetInt32("HomestayId"),r.GetString("HomestayName"),r.GetString("OwnerName"),r.GetInt32("Year"),r.GetInt32("Month"),r.GetByte(r.GetOrdinal("PeriodNo")),r.GetEnum<SettlementStatus>("Status"),r.GetDecimal("TotalRoomAmount"),r.GetDecimal("TotalRetained"),r.GetDecimal("TotalCommission"),r.GetDecimal("TotalDiscount"),r.GetDecimal("PayoutAmount"),r.GetNullableUtcDateTime("SentAt"),r.GetNullableUtcDateTime("PaidAt"),r.GetNullableString("PaidBankRef"),r.GetNullableString("OwnerFeedback"),r.GetNullableString("OwnerBankName"),r.GetNullableString("OwnerBankAccountNo"),r.GetNullableString("OwnerBankAccountName"),r.GetRowVersion("RowVer"));
    private static SettlementListItem ToItem(SettlementHead h)=>new(h.Id,h.HomestayId,h.HomestayName,h.OwnerName,h.Year,h.Month,h.PeriodNo,h.Status,new(h.TotalRoomAmount,h.TotalRetained,h.TotalCommission,h.TotalDiscount,h.PayoutAmount),h.SentAt,h.PaidAt,h.RowVer);
}
