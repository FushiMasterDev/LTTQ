using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

public sealed class DatabaseInfrastructureTests
{
    [SqlFact]
    public async Task DuplicateRoomNightHasNamedConstraintAndBusinessMessage()
    {
        await using var uow = await UnitOfWork.BeginAsync(SqlFixture.Factory(), SqlFixture.Token);
        var fixture = await SqlFixture.CreateBookingAsync(uow, SqlFixture.Token);
        using var command = uow.CreateCommand("INSERT dbo.RoomNights (RoomId, [Date], BookingId) VALUES (@room, '2099-01-01', @booking);");
        command.Parameters.AddInt32("@room", fixture.RoomId);
        command.Parameters.AddInt32("@booking", fixture.BookingId);
        await uow.ExecuteAsync(command, SqlFixture.Token);
        var error = await Assert.ThrowsAsync<DbConstraintViolationException>(() => uow.ExecuteAsync(command, SqlFixture.Token));
        Assert.Equal("PK_RoomNights", error.ConstraintName);
        Assert.Equal(DbConstraintKind.PrimaryKey, error.Kind);
        var business = await Assert.ThrowsAsync<BusinessException>(() => BusinessErrorTranslator.ExecuteAsync(ct => uow.ExecuteAsync(command, ct), SqlFixture.Token));
        Assert.Equal(ConstraintMessages.GetMessage("PK_RoomNights"), business.Message);
        Assert.Equal("PK_RoomNights", business.ConstraintName);
    }

    [SqlFact]
    public async Task SecondDepositHasNamedUniqueIndexAndBusinessMessage()
    {
        await using var uow = await UnitOfWork.BeginAsync(SqlFixture.Factory(), SqlFixture.Token);
        var fixture = await SqlFixture.CreateBookingAsync(uow, SqlFixture.Token);
        using var command = uow.CreateCommand("""
            INSERT dbo.Payments (VoucherNo, BookingId, Direction, Type, Method, Amount, Status, RecordedByUserId, RecordedAt)
            VALUES (@voucher, @booking, 'In', 'Deposit', 'BankTransfer', 300000, 'Pending', @user, SYSUTCDATETIME());
            """);
        var voucher = command.Parameters.AddVarChar("@voucher", 20, "T" + Guid.NewGuid().ToString("N")[..18]);
        command.Parameters.AddInt32("@booking", fixture.BookingId);
        command.Parameters.AddInt32("@user", fixture.UserId);
        await uow.ExecuteAsync(command, SqlFixture.Token);
        voucher.Value = "T" + Guid.NewGuid().ToString("N")[..18];
        var error = await Assert.ThrowsAsync<DbConstraintViolationException>(() => uow.ExecuteAsync(command, SqlFixture.Token));
        Assert.Equal("UX_Payments_OneDeposit", error.ConstraintName);
        Assert.Equal(DbConstraintKind.Unique, error.Kind);
        var business = await Assert.ThrowsAsync<BusinessException>(() => BusinessErrorTranslator.ExecuteAsync(ct => uow.ExecuteAsync(command, ct), SqlFixture.Token));
        Assert.Equal("Đơn đã có phiếu cọc đang chờ hoặc đã xác minh.", business.Message);
    }

    [SqlFact]
    public async Task CheckViolationUsesGenericBusinessMessage()
    {
        await using var uow = await UnitOfWork.BeginAsync(SqlFixture.Factory(), SqlFixture.Token);
        using var command = uow.CreateCommand("INSERT dbo.Counters (CounterKey, LastValue) VALUES (@key, -1);");
        command.Parameters.AddVarChar("@key", 30, Guid.NewGuid().ToString("N")[..30]);
        var error = await Assert.ThrowsAsync<DbConstraintViolationException>(() => uow.ExecuteAsync(command, SqlFixture.Token));
        Assert.Equal(DbConstraintKind.Check, error.Kind);
        Assert.Equal("CK_Counters_LastValue", error.ConstraintName);
        var business = await Assert.ThrowsAsync<BusinessException>(() => BusinessErrorTranslator.ExecuteAsync(ct => uow.ExecuteAsync(command, ct), SqlFixture.Token));
        Assert.Equal(ConstraintMessages.DefaultMessage, business.Message);
    }

    [SqlFact]
    public async Task DisposeWithoutCommitRollsBackEvenAfterCancellation()
    {
        var factory = SqlFixture.Factory();
        var key = Guid.NewGuid().ToString("N")[..30];
        using var cancelled = new CancellationTokenSource();
        await using (var uow = await UnitOfWork.BeginAsync(factory, cancelled.Token))
        {
            using var command = uow.CreateCommand("INSERT dbo.Counters (CounterKey, LastValue) VALUES (@key, 0);");
            command.Parameters.AddVarChar("@key", 30, key);
            await uow.ExecuteAsync(command, cancelled.Token);
            cancelled.Cancel();
        }
        var count = await factory.ReadAsync(async (db, ct) =>
        {
            using var command = db.CreateCommand("SELECT COUNT(*) FROM dbo.Counters WHERE CounterKey=@key;");
            command.Parameters.AddVarChar("@key", 30, key);
            return (int)(await db.ScalarAsync(command, ct))!;
        }, SqlFixture.Token);
        Assert.Equal(0, count);
    }

    [SqlFact]
    public async Task TransactionLockTimesOutOnAnotherConnectionAndIsReleasedOnDispose()
    {
        var factory = SqlFixture.Factory();
        var resource = "Test:" + Guid.NewGuid();
        await using var second = await UnitOfWork.BeginAsync(factory, SqlFixture.Token);
        await using (var first = await UnitOfWork.BeginAsync(factory, SqlFixture.Token))
        {
            await first.AcquireAppLockAsync(resource, 0, SqlFixture.Token);
            var error = await Assert.ThrowsAsync<DbLockTimeoutException>(() => second.AcquireAppLockAsync(resource, 100, SqlFixture.Token));
            Assert.Equal(-1, error.ReturnCode);
            Assert.Equal(resource, error.Resource);
        }
        await second.AcquireAppLockAsync(resource, 100, SqlFixture.Token);
    }

    [SqlFact]
    public async Task ClockIsUtcAndNearCurrentTimeInReadAndTransaction()
    {
        var factory = SqlFixture.Factory();
        var clock = new DbClock(factory);
        await using var uow = await UnitOfWork.BeginAsync(factory, SqlFixture.Token);
        var time = await clock.GetUtcNowAsync(uow, SqlFixture.Token);
        Assert.Equal(DateTimeKind.Utc, time.Kind);
        Assert.InRange((time - DateTime.UtcNow).Duration(), TimeSpan.Zero, TimeSpan.FromMinutes(2));
        Assert.Equal(DateTimeKind.Utc, (await clock.GetUtcNowAsync(SqlFixture.Token)).Kind);
        using var isolation = uow.CreateCommand("SELECT transaction_isolation_level FROM sys.dm_exec_sessions WHERE session_id=@@SPID;");
        Assert.Equal(2, Convert.ToInt32(await uow.ScalarAsync(isolation, SqlFixture.Token)));
    }

    [SqlFact]
    public async Task AuditWorksAsApplicationUserAndKeepsVietnameseJson()
    {
        await using var uow = await UnitOfWork.BeginAsync(SqlFixture.Factory(), SqlFixture.Token);
        var recordId = Guid.NewGuid().ToString("N");
        using var identity = uow.CreateCommand("SELECT USER_NAME();");
        var impersonate = !string.Equals((string?)(await uow.ScalarAsync(identity, SqlFixture.Token)), "homestay_app", StringComparison.OrdinalIgnoreCase);
        if (impersonate)
        {
            using var assume = uow.CreateCommand("EXECUTE AS USER = 'homestay_app';");
            await uow.ExecuteAsync(assume, SqlFixture.Token);
        }
        try
        {
            using var permissions = uow.CreateCommand("SELECT HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'INSERT') AS I, HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'UPDATE') AS U, HAS_PERMS_BY_NAME('dbo.AuditLogs', 'OBJECT', 'DELETE') AS D;");
            var grants = Assert.Single(await uow.QueryAsync(permissions, r => (r.GetInt32("I"), r.GetInt32("U"), r.GetInt32("D")), SqlFixture.Token));
            Assert.Equal((1, 0, 0), grants);
            await new AuditWriter().WriteAsync(uow, new AuditEntry(null, "Test.Vietnamese", "Test", recordId, new { Ten = "Tên cũ" }, new { Ten = "Khách Đà Lạt" }, "Kiểm thử", "TestMachine"), SqlFixture.Token);
        }
        finally
        {
            if (impersonate)
            {
                using var revert = uow.CreateCommand("REVERT;");
                await uow.ExecuteAsync(revert, SqlFixture.Token);
            }
        }
        using var read = uow.CreateCommand("SELECT UserId, OldValues, NewValues FROM dbo.AuditLogs WHERE RecordId=@id;");
        read.Parameters.AddVarChar("@id", 50, recordId);
        var row = Assert.Single(await uow.QueryAsync(read, r => (User: r.GetNullableInt32("UserId"), Old: r.GetString("OldValues"), New: r.GetString("NewValues")), SqlFixture.Token));
        Assert.Null(row.User);
        Assert.Contains("Tên cũ", row.Old);
        Assert.Contains("Khách Đà Lạt", row.New);
        Assert.DoesNotContain("\\u", row.New);
    }

    [SqlFact]
    public async Task DataScopeHidesOutOfScopeBookingsAndEnforcesDraftAndReadOnly()
    {
        await using var uow = await UnitOfWork.BeginAsync(SqlFixture.Factory(), SqlFixture.Token);
        var fixture = await SqlFixture.CreateBookingAsync(uow, SqlFixture.Token);
        var current = new CurrentSession();
        var scope = new DataScope(current);
        current.Set(new SessionInfo { Position = Position.Staff, EmployeeId = -1 });
        await Assert.ThrowsAsync<NotFoundException>(() => scope.EnsureBookingWritableAsync(uow, fixture.BookingId, SqlFixture.Token));
        current.Set(new SessionInfo { Position = Position.Staff, EmployeeId = fixture.EmployeeId });
        await scope.EnsureBookingWritableAsync(uow, fixture.BookingId, SqlFixture.Token);
        await scope.EnsureHomestayEditableAsync(uow, fixture.HomestayId, SqlFixture.Token);
        using var submitted = uow.CreateCommand("UPDATE dbo.Homestays SET Status='PendingReview' WHERE Id=@id;");
        submitted.Parameters.AddInt32("@id", fixture.HomestayId);
        await uow.ExecuteAsync(submitted, SqlFixture.Token);
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.EnsureHomestayEditableAsync(uow, fixture.HomestayId, SqlFixture.Token));
        current.Set(new SessionInfo { Position = Position.Manager, HasAllAreas = true });
        await scope.EnsureHomestayEditableAsync(uow, fixture.HomestayId, SqlFixture.Token);
        await scope.EnsureBookingWritableAsync(uow, fixture.BookingId, SqlFixture.Token);
        current.Set(new SessionInfo { Position = Position.Accountant });
        await Assert.ThrowsAsync<ForbiddenException>(() => scope.EnsureBookingWritableAsync(uow, fixture.BookingId, SqlFixture.Token));
    }
}
