using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed record BookingReadResult(BookingDetail Detail, bool InScope);

public sealed partial class BookingRepository
{
    private const string From = """
         FROM dbo.Bookings b JOIN dbo.Customers c ON c.Id=b.CustomerId JOIN dbo.Homestays h ON h.Id=b.HomestayId
         JOIN dbo.RoomTypes rt ON rt.Id=b.RoomTypeId JOIN dbo.Rooms r ON r.Id=b.RoomId JOIN dbo.Owners o ON o.Id=h.OwnerId
         JOIN dbo.Employees creator ON creator.Id=b.CreatedByEmployeeId LEFT JOIN dbo.Employees assignee ON assignee.Id=b.AssignedEmployeeId
         JOIN dbo.vw_BookingPaymentState ps ON ps.BookingId=b.Id
        """;
    private const string Columns = """
        b.Id,b.Code,b.CustomerId,c.FullName AS CustomerName,c.Phone AS CustomerPhone,b.HomestayId,h.Name AS HomestayName,h.AreaId,
        b.RoomTypeId,rt.Name AS RoomTypeName,b.RoomId,r.RoomCode,b.CheckIn,b.CheckOut,b.Status,ps.PaymentState,ps.PaymentStateName,
        b.CreatedByEmployeeId,creator.FullName AS CreatedByName,b.AssignedEmployeeId,assignee.FullName AS AssignedEmployeeName,
        CASE WHEN b.Status='AwaitingPayment' AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending') THEN b.HoldExpiresAt
            WHEN b.Status='Deposited' THEN b.ConfirmDeadlineAt END AS DeadlineAt,
        CAST(CASE WHEN b.Status='Deposited' AND b.ConfirmDeadlineAt<@now THEN 1 ELSE 0 END AS bit) AS IsConfirmationOverdue,
        b.ContactName,b.ContactPhone,h.ContactPhone AS HomestayPhone,o.Phone AS OwnerPhone,b.RowVer
        """;
    private static BookingListItem Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32("Id"),
        Code = r.GetString("Code"),
        CustomerId = r.GetInt32("CustomerId"),
        CustomerName = r.GetString("CustomerName"),
        CustomerPhone = r.GetString("CustomerPhone"),
        HomestayId = r.GetInt32("HomestayId"),
        HomestayName = r.GetString("HomestayName"),
        AreaId = r.GetInt32("AreaId"),
        RoomTypeId = r.GetInt32("RoomTypeId"),
        RoomTypeName = r.GetString("RoomTypeName"),
        RoomId = r.GetInt32("RoomId"),
        RoomCode = r.GetString("RoomCode"),
        CheckIn = r.GetDateOnly("CheckIn"),
        CheckOut = r.GetDateOnly("CheckOut"),
        Status = r.GetEnum<BookingStatus>("Status"),
        PaymentState = r.GetEnum<PaymentState>("PaymentState"),
        PaymentStateName = r.GetString("PaymentStateName"),
        CreatedByEmployeeId = r.GetInt32("CreatedByEmployeeId"),
        CreatedByName = r.GetString("CreatedByName"),
        AssignedEmployeeId = r.GetNullableInt32("AssignedEmployeeId"),
        AssignedEmployeeName = r.GetNullableString("AssignedEmployeeName"),
        DeadlineAtUtc = r.GetNullableUtcDateTime("DeadlineAt"),
        IsConfirmationOverdue = r.GetBoolean("IsConfirmationOverdue"),
        ContactName = r.GetString("ContactName"),
        ContactPhone = r.GetString("ContactPhone"),
        HomestayPhone = r.GetNullableString("HomestayPhone"),
        OwnerPhone = r.GetString("OwnerPhone"),
        RowVer = r.GetRowVersion("RowVer")
    };
    internal static string QueueCondition(BookingQueue queue) => queue switch
    {
        BookingQueue.Mine => "b.AssignedEmployeeId=@employee",
        BookingQueue.Unassigned => "b.AssignedEmployeeId IS NULL",
        BookingQueue.AwaitingPayment => "b.Status='AwaitingPayment'",
        BookingQueue.DepositVerification => "(b.Status='PaymentSubmitted' OR ps.PaymentState='DepositPending')",
        BookingQueue.OwnerConfirmation => "b.Status='Deposited'",
        BookingQueue.Upcoming => "b.Status='Confirmed' AND b.CheckIn>=@today",
        BookingQueue.InHouse => "b.Status='CheckedIn'",
        BookingQueue.ArrivalsToday => "b.Status='Confirmed' AND b.CheckIn=@today",
        BookingQueue.DeparturesToday => "b.Status='CheckedIn' AND b.CheckOut=@today",
        BookingQueue.OpenChangeRequests => "EXISTS(SELECT 1 FROM dbo.BookingChangeRequests cr WHERE cr.BookingId=b.Id AND cr.Status IN ('Pending','AwaitingPayment'))",
        _ => "1=1"
    };
    public async Task<IReadOnlyList<BookingListItem>> QuickSearchByCodeAsync(DbSession db, string code, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT TOP (5) {Columns}{From} WHERE REPLACE(b.Code,'-','')=@code ORDER BY b.Code,b.Id;");
        command.Parameters.AddVarChar("@code", 20, StaffReadSql.NormalizeCode(code));
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(command, Map, ct).ConfigureAwait(false);
    }
    public async Task<PagedResult<BookingListItem>> ListAsync(DbSession db, BookingSearchRequest request, string scopeCondition,
        Action<SqlCommand> addScope, int employeeId, DateTime now, CancellationToken ct)
    {
        var exact = !string.IsNullOrWhiteSpace(request.Code) || !string.IsNullOrWhiteSpace(request.Phone);
        var where = $"""
             WHERE {(exact ? "1=1" : $"({scopeCondition}) AND ({QueueCondition(request.Queue)})")}
             AND (@code='' OR REPLACE(b.Code,'-','')=@code) AND (@phone IS NULL OR c.Phone=@phone)
             AND (@name=N'' OR c.FullName COLLATE Vietnamese_CI_AI LIKE @name ESCAPE '~')
             AND (@home IS NULL OR b.HomestayId=@home) AND (@from IS NULL OR b.CheckIn>=@from) AND (@to IS NULL OR b.CheckIn<=@to)
             AND (@status IS NULL OR b.Status=@status)
             AND (@search=N'' OR b.Code LIKE @search ESCAPE '~' OR c.Phone LIKE @search ESCAPE '~' OR c.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR h.Name COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~')
             AND (@expiring=0 OR (b.Status='AwaitingPayment' AND b.HoldExpiresAt>=@now AND b.HoldExpiresAt<DATEADD(minute,5,@now)
                 AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')))
             AND (@overdue=0 OR (b.Status='Deposited' AND b.ConfirmDeadlineAt<@now))
             AND (@mine=0 OR b.AssignedEmployeeId=@employee)
            """;
        void Parameters(SqlCommand command)
        {
            if (!exact) addScope(command);
            command.Parameters.AddInt32("@employee", employeeId);
            command.Parameters.AddUtcDateTime("@now", now);
            command.Parameters.AddDate("@today", DateOnly.FromDateTime(now.AddHours(7)));
            command.Parameters.AddVarChar("@code", 20, StaffReadSql.NormalizeCode(request.Code));
            command.Parameters.AddVarChar("@phone", 16, request.Phone);
            command.Parameters.AddNVarChar("@name", 500, StaffReadSql.Like(request.CustomerName));
            command.Parameters.AddInt32("@home", request.HomestayId);
            command.Parameters.AddDate("@from", request.CheckInFrom);
            command.Parameters.AddDate("@to", request.CheckInTo);
            command.Parameters.AddEnum("@status", 20, request.Status);
            command.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
            command.Parameters.AddBoolean("@expiring", request.OnlyExpiringHolds);
            command.Parameters.AddBoolean("@overdue", request.OnlyOverdueConfirmation);
            command.Parameters.AddBoolean("@mine", request.AssignedToMeOnly);
        }
        var column = request.Page.SortColumn switch
        {
            "CustomerName" => "c.FullName",
            "CustomerPhone" => "c.Phone",
            "HomestayName" => "h.Name",
            "RoomTypeName" => "rt.Name",
            "RoomCode" => "r.RoomCode",
            "CheckIn" => "b.CheckIn",
            "CheckOut" => "b.CheckOut",
            "Status" => "b.Status",
            "PaymentState" => "ps.PaymentState",
            "AssignedEmployeeName" => "assignee.FullName",
            "DeadlineAtUtc" => "DeadlineAt",
            _ => "b.Code"
        };
        using var count = db.CreateCommand("SELECT COUNT(*)" + From + where);
        Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        using var command = db.CreateCommand($"SELECT {Columns}{From}{where} ORDER BY {column} {(request.Page.SortDescending ? "DESC" : "ASC")},b.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command);
        StaffReadSql.PageParameters(command, request.Page);
        return new() { TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize, Items = await db.QueryAsync(command, Map, ct).ConfigureAwait(false) };
    }

    public async Task<BookingReadResult?> GetAsync(DbSession db, int? id, string? code, string scopeCondition, Action<SqlCommand> addScope,
        int? auditUserId, bool includeAudit, DateTime now, CancellationToken ct)
    {
        using var head = db.CreateCommand($"""
            SELECT {Columns},CAST(CASE WHEN {scopeCondition} THEN 1 ELSE 0 END AS bit) AS InScope,
                b.Guests,b.SourceChannel,b.SpecialRequests,h.Address,h.CheckinGuide,h.CheckinTime,h.CheckoutTime,
                b.HoldExpiresAt,b.ConfirmDeadlineAt,b.TotalAmount,b.DepositRate,b.DepositAmount,b.DiscountAmount,ps.AmountDue,b.CommissionRate,b.CancellationPolicyId,
                b.VoucherId,(SELECT v.Code FROM dbo.Vouchers v WHERE v.Id=b.VoucherId) AS VoucherCode
            {From} WHERE (@id IS NOT NULL AND b.Id=@id) OR (@id IS NULL AND REPLACE(b.Code,'-','')=@code);
            """);
        head.Parameters.AddInt32("@id", id);
        head.Parameters.AddVarChar("@code", 20, StaffReadSql.NormalizeCode(code));
        head.Parameters.AddUtcDateTime("@now", now);
        addScope(head);
        var row = (await db.QueryAsync(head, r => (Booking: Map(r), InScope: r.GetBoolean("InScope"), Guests: r.GetInt32("Guests"), Source: r.GetEnum<SourceChannel>("SourceChannel"),
            Special: r.GetNullableString("SpecialRequests"), Address: r.GetString("Address"), Guide: r.GetNullableString("CheckinGuide"),
            InTime: TimeOnly.FromTimeSpan(r.GetTimeSpan(r.GetOrdinal("CheckinTime"))), OutTime: TimeOnly.FromTimeSpan(r.GetTimeSpan(r.GetOrdinal("CheckoutTime"))),
            Hold: r.GetUtcDateTime("HoldExpiresAt"), Confirm: r.GetNullableUtcDateTime("ConfirmDeadlineAt"), Total: r.GetDecimal("TotalAmount"), Rate: r.GetDecimal("DepositRate"),
            Deposit: r.GetDecimal("DepositAmount"), Discount: r.GetDecimal("DiscountAmount"), Due: r.GetDecimal("AmountDue"), Commission: r.GetDecimal("CommissionRate"), Policy: r.GetInt32("CancellationPolicyId"),
            VoucherId: r.GetNullableInt32("VoucherId"), VoucherCode: r.GetNullableString("VoucherCode")), ct).ConfigureAwait(false)).SingleOrDefault();
        if (row.Booking is null) return null;
        var bookingId = row.Booking.Id;
        var customer = await new CustomerRepository().GetAsync(db, row.Booking.CustomerId, false, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Booking customer missing.");
        using var nights = db.CreateCommand("SELECT [Date],DayType,Price FROM dbo.BookingNights WHERE BookingId=@id ORDER BY [Date];");
        nights.Parameters.AddInt32("@id", bookingId);
        var nightRows = await db.QueryAsync(nights, r => new NightlyPrice(r.GetDateOnly("Date"), r.GetDateOnly("Date").DayOfWeek, r.GetEnum<DayType>("DayType"), r.GetDecimal("Price")), ct).ConfigureAwait(false);
        using var policy = db.CreateCommand("""
            SELECT p.Id,p.HomestayId,p.Name,p.Scope,p.IsActive,r.MinDaysBefore,r.RefundPercent FROM dbo.CancellationPolicies p
            LEFT JOIN dbo.CancellationRules r ON r.PolicyId=p.Id WHERE p.Id=@id ORDER BY r.MinDaysBefore DESC;
            """);
        policy.Parameters.AddInt32("@id", row.Policy);
        var policyRows = await db.QueryAsync(policy, r => (Id: r.GetInt32("Id"), Home: r.GetNullableInt32("HomestayId"), Name: r.GetString("Name"), Scope: r.GetEnum<CancellationScope>("Scope"),
            Active: r.GetBoolean("IsActive"), Days: r.GetNullableInt32("MinDaysBefore"), Percent: r.GetNullableInt32("RefundPercent")), ct).ConfigureAwait(false);
        var p = policyRows.First();
        using var payments = db.CreateCommand("""
            SELECT p.Id,p.VoucherNo,p.Type,p.Amount,p.Method,p.Status,p.RecordedByUserId,COALESCE(re.FullName,ru.Username) AS RecordedByName,p.RecordedAt,
                p.VerifiedByUserId,COALESCE(ve.FullName,vu.Username) AS VerifiedByName,p.VerifiedAt,p.EvidenceFileId,p.TransferContent
            FROM dbo.Payments p JOIN dbo.Users ru ON ru.Id=p.RecordedByUserId LEFT JOIN dbo.Employees re ON re.UserId=ru.Id
            LEFT JOIN dbo.Users vu ON vu.Id=p.VerifiedByUserId LEFT JOIN dbo.Employees ve ON ve.UserId=vu.Id
            WHERE p.BookingId=@id ORDER BY p.RecordedAt DESC,p.Id DESC;
            """);
        payments.Parameters.AddInt32("@id", bookingId);
        var paymentRows = await db.QueryAsync(payments, r => new BookingPaymentItem(r.GetInt32("Id"), r.GetString("VoucherNo"), r.GetEnum<PaymentType>("Type"), r.GetDecimal("Amount"),
            r.GetEnum<PaymentMethod>("Method"), r.GetEnum<PaymentStatus>("Status"), r.GetInt32("RecordedByUserId"), r.GetString("RecordedByName"), r.GetUtcDateTime("RecordedAt"),
            r.GetNullableInt32("VerifiedByUserId"), r.GetNullableString("VerifiedByName"), r.GetNullableUtcDateTime("VerifiedAt"),
            r.IsDBNull(r.GetOrdinal("EvidenceFileId")) ? null : r.GetGuid("EvidenceFileId"), !r.IsDBNull(r.GetOrdinal("EvidenceFileId")), r.GetNullableString("TransferContent")), ct).ConfigureAwait(false);
        using var history = db.CreateCommand("""
            SELECT sh.*,COALESCE(e.FullName,u.Username) AS ActorName FROM dbo.BookingStatusHistories sh
            LEFT JOIN dbo.Users u ON u.Id=sh.ActorUserId LEFT JOIN dbo.Employees e ON e.UserId=u.Id WHERE sh.BookingId=@id ORDER BY sh.ChangedAt,sh.Id;
            """);
        history.Parameters.AddInt32("@id", bookingId);
        var historyRows = await db.QueryAsync(history, r => new BookingHistoryItem(r.GetInt64("Id"), r.GetNullableString("FromStatus") is { } s ? Enum.Parse<BookingStatus>(s) : null,
            r.GetEnum<BookingStatus>("ToStatus"), r.GetEnum<ActorType>("ActorType"), r.GetNullableString("ActorName"), r.GetBoolean("OnBehalfOfOwner"), r.GetNullableString("Note"), r.GetUtcDateTime("ChangedAt")), ct).ConfigureAwait(false);
        using var audit = db.CreateCommand("""
            SELECT a.*,COALESCE(e.FullName,u.Username) AS ActorName FROM dbo.AuditLogs a
            LEFT JOIN dbo.Users u ON u.Id=a.UserId LEFT JOIN dbo.Employees e ON e.UserId=u.Id
            WHERE @include=1 AND a.TableName='Bookings' AND a.RecordId=@record AND (@user IS NULL OR a.UserId=@user) ORDER BY a.CreatedAt,a.Id;
            """);
        audit.Parameters.AddBoolean("@include", includeAudit);
        audit.Parameters.AddVarChar("@record", 50, bookingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        audit.Parameters.AddInt32("@user", auditUserId);
        var auditRows = await db.QueryAsync(audit, r => new BookingAuditItem(r.GetInt64("Id"), r.GetString("ActionCode"), r.GetNullableString("ActorName"), r.GetNullableString("OldValues"),
            r.GetNullableString("NewValues"), r.GetNullableString("Note"), r.GetUtcDateTime("CreatedAt")), ct).ConfigureAwait(false);
        using var changes = db.CreateCommand("SELECT Id,RequestType,Status,Reason,RequestedAt,PaymentDeadlineAt FROM dbo.BookingChangeRequests WHERE BookingId=@id ORDER BY RequestedAt DESC,Id DESC;");
        changes.Parameters.AddInt32("@id", bookingId);
        var changeRows = await db.QueryAsync(changes, r => new BookingChangeItem(r.GetInt32("Id"), r.GetEnum<ChangeRequestType>("RequestType"), r.GetEnum<ChangeRequestStatus>("Status"), r.GetString("Reason"),
            r.GetUtcDateTime("RequestedAt"), r.GetNullableUtcDateTime("PaymentDeadlineAt")), ct).ConfigureAwait(false);
        return new(new BookingDetail
        {
            Booking = row.Booking,
            Customer = customer,
            Guests = row.Guests,
            SourceChannel = row.Source,
            SpecialRequests = row.Special,
            Address = row.Address,
            CheckinGuide = row.Guide,
            CheckinTime = row.InTime,
            CheckoutTime = row.OutTime,
            HoldExpiresAtUtc = row.Hold,
            ConfirmDeadlineAtUtc = row.Confirm,
            Nights = nightRows,
            TotalAmount = row.Total,
            DepositRate = row.Rate,
            DepositAmount = row.Deposit,
            DiscountAmount = row.Discount,
            VoucherId = row.VoucherId,
            VoucherCode = row.VoucherCode,
            AmountDue = row.Due,
            CommissionRate = row.Commission,
            CancellationPolicy = new(p.Id, p.Home, p.Name, p.Scope, p.Active, policyRows.Where(x => x.Days.HasValue).Select(x => new CancellationRuleItem(x.Days!.Value, x.Percent!.Value)).ToArray()),
            Payments = paymentRows,
            History = historyRows,
            Audit = auditRows,
            ChangeRequests = changeRows
        }, row.InScope);
    }
}
