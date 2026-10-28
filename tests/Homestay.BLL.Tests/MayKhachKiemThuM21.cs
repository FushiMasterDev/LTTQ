using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Tests;

// Phiên thật qua AuthService/WriteSessionValidator, cùng cách M14TestClient; không validator giả.
/// <summary>Bộ khởi tạo các dịch vụ đặt phòng/thanh toán cho kiểm thử, dựa trên M14TestClient.</summary>
internal sealed class M21TestClient
{
    public M14TestClient Login { get; } = new();
    public ConnectionFactory Factory => Login.Factory;
    public CurrentSession Current => Login.Current;
    public AvailabilityService Availability { get; }
    public CustomerService Customers { get; }
    public BookingService Bookings { get; }
    public NotificationService Notifications { get; }
    public FileService Files { get; }
    public PaymentService Payments { get; }
    public ChangeRequestService Changes { get; }
    public M21TestClient()
    {
        var auth = new Authorizer(Current);
        var clock = new DbClock(Factory);
        var settings = new SettingsProvider(Factory);
        var customers = new CustomerRepository();
        var writes = new WriteOperationRunner(Factory, Current, new WriteSessionValidator(new AuthRepository()));
        Availability = new(auth, Current, Factory, new AvailabilityRepository(), clock, settings);
        Customers = new(auth, Current, Factory, writes, customers, clock, new AuditWriter(), Login.Machine);
        Bookings = new(auth, Current, new DataScope(Current), Factory, new BookingRepository(), customers, clock, settings, Availability, writes, new AuditWriter(), Login.Machine);
        Changes = new(auth, Current, new DataScope(Current), Factory, new ChangeRequestRepository(), Bookings);
        Notifications = new(Current, Factory, writes, new NotificationRepository(), clock);
        Files = new(auth, Factory, new EvidenceRepository());
        Payments = new(auth, Current, new DataScope(Current), writes, new BookingRepository(), new PaymentRepository(),
            new EvidenceRepository(), new NotificationRepository(), clock, Bookings, new AuditWriter(), Login.Machine);
    }
    public async Task<TestIdentity> LoginNewAsync(Position role = Position.Manager, int? areaId = null, bool allAreas = true)
    {
        var identity = await Login.CreateIdentityAsync(role);
        await MutateAsync("UPDATE dbo.Employees SET HasAllAreas=@all WHERE Id=@employee;", cmd =>
        {
            cmd.Parameters.AddBoolean("@all", allAreas);
            cmd.Parameters.AddInt32("@employee", identity.EmployeeId);
        });
        if (areaId.HasValue) await MutateAsync("INSERT dbo.EmployeeAreas(EmployeeId,AreaId,AssignedByEmployeeId,AssignedAt) VALUES(@employee,@area,@employee,SYSUTCDATETIME());", cmd =>
        {
            cmd.Parameters.AddInt32("@employee", identity.EmployeeId);
            cmd.Parameters.AddInt32("@area", areaId);
        });
        await Login.LoginAsync(identity);
        return identity;
    }
    public async Task MutateAsync(string sql, Action<SqlCommand>? parameters = null)
    {
        var db = await UnitOfWork.BeginAsync(Factory, CancellationToken.None);
        await using var scope = db.ConfigureAwait(false);
        using var command = db.CreateCommand(sql);
        parameters?.Invoke(command);
        await db.ExecuteAsync(command, CancellationToken.None);
        await db.CommitAsync(CancellationToken.None);
    }
    public Task<T> ScalarAsync<T>(string sql, Action<SqlCommand>? parameters = null) => Factory.ReadAsync(async (db, ct) =>
    {
        using var command = db.CreateCommand(sql);
        parameters?.Invoke(command);
        return (T)(await db.ScalarAsync(command, ct))!;
    }, CancellationToken.None);
}

/// <summary>Dữ liệu homestay/phòng/khách đã tạo sẵn để kiểm thử đặt phòng.</summary>
internal sealed record M21Data(int AreaId, int HomestayId, int RoomTypeId, int CategoryId, int AmenityId, int[] Rooms, int CustomerId)
{
    public static DateOnly CheckIn => new(2099, 1, 1);
    public static DateOnly CheckOut => CheckIn.AddDays(2);
    public static async Task<M21Data> CreateAsync(M21TestClient client, int roomCount = 4)
    {
        var db = await UnitOfWork.BeginAsync(client.Factory, CancellationToken.None);
        await using var scope = db.ConfigureAwait(false);
        using var command = db.CreateCommand("""
            INSERT dbo.Areas(Name,Province,AreaGroup,IsActive) VALUES(@name,N'Test M21','InnerCity',1);
            DECLARE @area int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.RoomCategories(Name,IsActive) VALUES(@name,1); DECLARE @category int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Amenities(Name,IsActive) VALUES(@name,1); DECLARE @amenity int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Owners(FullName,Phone,Status) VALUES(@name,@phone,'Pending'); DECLARE @owner int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Homestays(OwnerId,AreaId,Name,Address,ContactPhone,CheckinGuide,Status,CreatedByUserId)
            VALUES(@owner,@area,@name,N'Địa chỉ M21',@phone,N'Gọi lễ tân','Active',@user); DECLARE @home int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.HomestayAmenities(HomestayId,AmenityId) VALUES(@home,@amenity);
            INSERT dbo.Contracts(HomestayId,ContractNo,StartDate,EndDate,CommissionRate,Status,SignedByEmployeeId)
            VALUES(@home,@contract,'2098-01-01','2099-12-31',.12,'Active',@employee);
            INSERT dbo.CancellationPolicies(HomestayId,Scope,Name,IsActive) VALUES(@home,'All',@name,1); DECLARE @policy int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.CancellationRules(PolicyId,MinDaysBefore,RefundPercent) VALUES(@policy,7,100),(@policy,3,50),(@policy,0,0);
            INSERT dbo.RoomTypes(HomestayId,CategoryId,Name,MaxGuests,IsActive) VALUES(@home,@category,@name,4,1); DECLARE @type int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.RoomTypePrices(RoomTypeId,DayType,Price,EffectiveFrom,CreatedByUserId)
            VALUES(@type,'Weekday',1000000,'2098-01-01',@user),(@type,'Weekend',1200000,'2098-01-01',@user),(@type,'Holiday',1500000,'2098-01-01',@user);
            DECLARE @n int=1;
            WHILE @n<=@rooms BEGIN
                INSERT dbo.Rooms(RoomTypeId,RoomCode,IsActive) VALUES(@type,CONCAT(N'R',RIGHT(CONCAT('00',@n),3)),1); SET @n=@n+1;
            END;
            INSERT dbo.Customers(FullName,Phone,Email,IdNumber,CreatedByEmployeeId) VALUES(@name,@phone,N'client@example.test','123456789012',@employee);
            SELECT @area AS AreaId,@home AS HomestayId,@type AS RoomTypeId,@category AS CategoryId,@amenity AS AmenityId,CONVERT(int,SCOPE_IDENTITY()) AS CustomerId;
            SELECT Id FROM dbo.Rooms WHERE RoomTypeId=@type ORDER BY RoomCode;
            """);
        command.Parameters.AddNVarChar("@name", 100, "M21 " + Guid.NewGuid().ToString("N"));
        command.Parameters.AddVarChar("@phone", 16, Homestay.BLL.Rules.PhoneNumber.Normalize(M14TestClient.Phone()));
        command.Parameters.AddVarChar("@contract", 30, "C" + Guid.NewGuid().ToString("N")[..25]);
        command.Parameters.AddInt32("@user", client.Current.RequireSession().UserId);
        command.Parameters.AddInt32("@employee", client.Current.RequireSession().EmployeeId);
        command.Parameters.AddInt32("@rooms", roomCount);
        var data = await db.RunAsync(command, async (cmd, ct) =>
        {
            var reader = await cmd.ExecuteReaderAsync(ct);
            await using var readerScope = reader.ConfigureAwait(false);
            Assert.True(await reader.ReadAsync(ct));
            var area = reader.GetInt32("AreaId"); var home = reader.GetInt32("HomestayId"); var type = reader.GetInt32("RoomTypeId");
            var category = reader.GetInt32("CategoryId"); var amenity = reader.GetInt32("AmenityId"); var customer = reader.GetInt32("CustomerId");
            Assert.True(await reader.NextResultAsync(ct));
            var rooms = new List<int>();
            while (await reader.ReadAsync(ct)) rooms.Add(reader.GetInt32("Id"));
            return new M21Data(area, home, type, category, amenity, rooms.ToArray(), customer);
        }, CancellationToken.None);
        await db.CommitAsync(CancellationToken.None);
        return data;
    }
    public async Task<int> BookingAsync(M21TestClient client, int roomIndex, BookingStatus status = BookingStatus.AwaitingPayment,
        bool expiredHold = false, DateOnly? checkIn = null, DateOnly? checkOut = null, bool assigned = true)
    {
        var db = await UnitOfWork.BeginAsync(client.Factory, CancellationToken.None);
        await using var scope = db.ConfigureAwait(false);
        using var command = db.CreateCommand("""
            DECLARE @now datetime2=SYSUTCDATETIME();
            INSERT dbo.Bookings(Code,CustomerId,ContactName,ContactPhone,SourceChannel,CreatedByEmployeeId,AssignedEmployeeId,HomestayId,RoomTypeId,RoomId,
                CheckIn,CheckOut,Guests,Status,HoldExpiresAt,DepositedAt,ConfirmDeadlineAt,ConfirmedAt,CheckedInAt,CompletedAt,CancelledAt,CancelReason,
                TotalAmount,DepositRate,DepositAmount,CommissionRate,CancellationPolicyId)
            SELECT @code,@customer,N'Người ở M21',c.Phone,'Phone',@employee,CASE WHEN @assigned=1 THEN @employee END,@home,@type,@room,
                @in,@out,2,@status,DATEADD(minute,CASE WHEN @expired=1 THEN -1 ELSE 15 END,@now),
                CASE WHEN @status IN ('Deposited','Confirmed','CheckedIn','Completed','Rejected','NoShow') THEN @now END,
                CASE WHEN @status='Deposited' THEN DATEADD(minute,-1,@now) END,
                CASE WHEN @status IN ('Confirmed','CheckedIn','Completed','NoShow') THEN @now END,
                CASE WHEN @status IN ('CheckedIn','Completed') THEN @now END,CASE WHEN @status='Completed' THEN @now END,
                CASE WHEN @status='Cancelled' THEN @now END,CASE WHEN @status IN ('Cancelled','Rejected') THEN N'Test' END,
                2000000,.3,600000,.12,(SELECT Id FROM dbo.CancellationPolicies WHERE HomestayId=@home AND IsActive=1 AND Scope='All')
            FROM dbo.Customers c WHERE c.Id=@customer;
            DECLARE @booking int=CONVERT(int,SCOPE_IDENTITY());
            WITH dates AS(SELECT @in AS d UNION ALL SELECT DATEADD(day,1,d) FROM dates WHERE DATEADD(day,1,d)<@out)
            INSERT dbo.RoomNights(RoomId,[Date],BookingId) SELECT @room,d,@booking FROM dates OPTION(MAXRECURSION 31);
            INSERT dbo.BookingNights(BookingId,[Date],DayType,Price) SELECT @booking,[Date],'Weekday',1000000 FROM dbo.RoomNights WHERE BookingId=@booking;
            INSERT dbo.BookingStatusHistories(BookingId,ToStatus,ActorType,ActorUserId,ChangedAt) VALUES(@booking,@status,'Employee',@user,@now);
            INSERT dbo.AuditLogs(UserId,ActionCode,TableName,RecordId,MachineName) VALUES(@user,'M21.Test','Bookings',CONVERT(varchar(50),@booking),'M21-test');
            SELECT @booking;
            """);
        command.Parameters.AddVarChar("@code", 20, "T" + Guid.NewGuid().ToString("N")[..19]);
        command.Parameters.AddInt32("@customer", CustomerId); command.Parameters.AddInt32("@home", HomestayId); command.Parameters.AddInt32("@type", RoomTypeId);
        command.Parameters.AddInt32("@room", Rooms[roomIndex]); command.Parameters.AddInt32("@employee", client.Current.RequireSession().EmployeeId);
        command.Parameters.AddInt32("@user", client.Current.RequireSession().UserId); command.Parameters.AddBoolean("@assigned", assigned);
        command.Parameters.AddDate("@in", checkIn ?? CheckIn); command.Parameters.AddDate("@out", checkOut ?? CheckOut);
        command.Parameters.AddEnum("@status", 20, (BookingStatus?)status); command.Parameters.AddBoolean("@expired", expiredHold);
        var id = (int)(await db.ScalarAsync(command, CancellationToken.None))!;
        await db.CommitAsync(CancellationToken.None);
        return id;
    }

    public Task PendingDepositAsync(M21TestClient client, int bookingId) => client.MutateAsync("""
        INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,Status,RecordedByUserId,RecordedAt)
        VALUES(@voucher,@booking,'In','Deposit','BankTransfer',600000,'Pending',@user,SYSUTCDATETIME());
        """, cmd =>
        {
            cmd.Parameters.AddVarChar("@voucher", 20, "P" + Guid.NewGuid().ToString("N")[..19]);
            cmd.Parameters.AddInt32("@booking", bookingId); cmd.Parameters.AddInt32("@user", client.Current.RequireSession().UserId);
        });
    public Task BlockAsync(M21TestClient client, int roomIndex, RoomBlockReason reason, DateOnly? date = null) => client.MutateAsync("""
        INSERT dbo.RoomBlocks(RoomId,StartDate,EndDate,Reason,CreatedByUserId) VALUES(@room,@date,DATEADD(day,1,@date),@reason,@user);
        DECLARE @block int=CONVERT(int,SCOPE_IDENTITY()); INSERT dbo.RoomNights(RoomId,[Date],RoomBlockId) VALUES(@room,@date,@block);
        """, cmd =>
        {
            cmd.Parameters.AddInt32("@room", Rooms[roomIndex]); cmd.Parameters.AddDate("@date", date ?? CheckIn);
            cmd.Parameters.AddEnum("@reason", 20, (RoomBlockReason?)reason); cmd.Parameters.AddInt32("@user", client.Current.RequireSession().UserId);
        });
}
