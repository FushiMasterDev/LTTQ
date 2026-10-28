using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M3.1: các hàm đọc phục vụ vòng đời đơn (người có thể giao, chi tiết theo phạm vi).</summary>
[Collection("M23 SQL")]
public sealed class M31ContractReadTests
{
    private static Task<IReadOnlyList<T>> Rows<T>(M21TestClient c, string sql, Func<SqlDataReader, T> map, Action<SqlCommand>? parameters = null) =>
        c.Factory.ReadAsync(async (db, ct) =>
        {
            using var cmd = db.CreateCommand(sql); parameters?.Invoke(cmd);
            return (IReadOnlyList<T>)await db.QueryAsync(cmd, map, ct);
        }, CancellationToken.None);
    private static Task<IReadOnlyList<int>> SeedBookingIds(M21TestClient c) => Rows(c, """
        SELECT b.Id FROM dbo.Bookings b JOIN dbo.Employees e ON e.Id=b.CreatedByEmployeeId
        WHERE e.EmployeeCode LIKE 'NV%' ORDER BY b.Id;
        """, r => r.GetInt32("Id"));
    private static Task SetAll(M21TestClient c, TestIdentity employee, bool all) => c.MutateAsync(
        "UPDATE dbo.Employees SET HasAllAreas=@all WHERE Id=@id;", cmd =>
        { cmd.Parameters.AddBoolean("@all", all); cmd.Parameters.AddInt32("@id", employee.EmployeeId); });
    private static Task AddArea(M21TestClient c, TestIdentity employee, int area) => c.MutateAsync("""
        INSERT dbo.EmployeeAreas(EmployeeId,AreaId,AssignedByEmployeeId,AssignedAt) VALUES(@id,@area,@by,SYSUTCDATETIME());
        """, cmd => { cmd.Parameters.AddInt32("@id", employee.EmployeeId); cmd.Parameters.AddInt32("@area", area); cmd.Parameters.AddInt32("@by", c.Current.RequireSession().EmployeeId); });

    [SqlFact]
    public async Task AssigneesUseBookingAssignAndExactActiveAreaEligibleOperatorsOnly()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup);
        var staff = new M21TestClient(); var actor = await staff.LoginNewAsync(Position.Staff, d.AreaId, false);
        var all = await setup.Login.CreateIdentityAsync(Position.Staff);
        var local = await setup.Login.CreateIdentityAsync(Position.Manager); await SetAll(setup, local, false); await AddArea(setup, local, d.AreaId);
        var outside = await setup.Login.CreateIdentityAsync(Position.Staff); await SetAll(setup, outside, false);
        var accountant = await setup.Login.CreateIdentityAsync(Position.Accountant);
        var locked = await setup.Login.CreateIdentityAsync(Position.Staff);
        var disabled = await setup.Login.CreateIdentityAsync(Position.Manager);
        var suspended = await setup.Login.CreateIdentityAsync(Position.Staff);
        var resigned = await setup.Login.CreateIdentityAsync(Position.Manager);
        await setup.Login.ExecuteAsync("UPDATE dbo.Users SET Status='Locked',LockReason=N'Test' WHERE Id=@id;", locked.UserId);
        await setup.Login.ExecuteAsync("UPDATE dbo.Users SET Status='Disabled' WHERE Id=@id;", disabled.UserId);
        await setup.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Suspended' WHERE Id=@id;", suspended.EmployeeId);
        await setup.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Resigned' WHERE Id=@id;", resigned.EmployeeId);
        var id = await d.BookingAsync(setup, 0);
        await setup.Bookings.ReassignAsync(new(id, actor.EmployeeId, (await setup.Bookings.GetAsync(id)).Booking.RowVer));
        Assert.False(new Authorizer(staff.Current).Can(Permission.EmployeeManage));
        var actual = await staff.Bookings.ListAssigneesAsync(id);
        Assert.Contains(actual, x => x.Id == all.EmployeeId); Assert.Contains(actual, x => x.Id == local.EmployeeId);
        foreach (var bad in new[] { actor, outside, accountant, locked, disabled, suspended, resigned }) Assert.DoesNotContain(actual, x => x.Id == bad.EmployeeId);
        var expected = await Rows(setup, """
            SELECT e.Id,e.EmployeeCode + N' · ' + e.FullName AS Name FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId
            WHERE e.EmploymentStatus='Active' AND u.Status='Active' AND e.Position IN ('Staff','Manager')
            AND (e.HasAllAreas=1 OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea WHERE ea.EmployeeId=e.Id AND ea.AreaId=@area))
            AND e.Id<>@actor ORDER BY e.EmployeeCode,e.Id;
            """, r => (Id: r.GetInt32("Id"), Name: r.GetString("Name")), cmd =>
            { cmd.Parameters.AddInt32("@area", d.AreaId); cmd.Parameters.AddInt32("@actor", actor.EmployeeId); });
        Assert.Equal(expected, actual.Select(x => (x.Id, x.Name)));
        Assert.All(actual, x => { Assert.NotNull(x.Code); Assert.StartsWith(x.Code + " · ", x.Name); });
        Assert.Equal(actual.Select(x => x.Id), (await setup.Bookings.ListAssigneesAsync(id)).Select(x => x.Id));
        var version = (await staff.Bookings.GetAsync(id)).Booking.RowVer;
        await Assert.ThrowsAsync<BusinessException>(() => staff.Bookings.ReassignAsync(new(id, outside.EmployeeId, version)));
        Assert.Equal(actor.EmployeeId, (await setup.Bookings.GetAsync(id)).Booking.AssignedEmployeeId);
        await staff.Bookings.ReassignAsync(new(id, local.EmployeeId, version));
    }

    [SqlFact]
    public async Task AssigneesHideOutOfScopeBookingAndRequireStaffToBeCurrentAssignee()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup);
        var id = await d.BookingAsync(setup, 0);
        var staff = new M21TestClient(); await staff.LoginNewAsync(Position.Staff, d.AreaId, false);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Bookings.ListAssigneesAsync(id));
        var outside = new M21TestClient(); await outside.LoginNewAsync(Position.Staff, allAreas: false);
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Bookings.ListAssigneesAsync(id));
        await Assert.ThrowsAsync<NotFoundException>(() => setup.Bookings.ListAssigneesAsync(int.MaxValue));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.ListAssigneesAsync(id));
    }

    [SqlFact]
    public async Task PendingReceiptAndRefundQueuesAndDetailMatchSeedAndDatabaseCounts()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(Position.Accountant);
        var seed = (await SeedBookingIds(c)).ToHashSet(); Assert.NotEmpty(seed);
        foreach (var refunds in new[] { false, true })
        {
            var expected = await Rows(c, """
                SELECT p.Id,p.BookingId,p.Amount,p.PayerOrPayeeAccount FROM dbo.Payments p
                WHERE p.Status='Pending' AND p.Type=@type ORDER BY p.RecordedAt,p.Id;
                """, r => (Id: r.GetInt32("Id"), BookingId: r.GetInt32("BookingId"), Amount: r.GetDecimal("Amount"), Account: r.GetNullableString("PayerOrPayeeAccount")),
                cmd => cmd.Parameters.AddVarChar("@type", 20, refunds ? "Refund" : "Deposit"));
            var request = new PaymentQueueRequest { Page = new() { PageSize = 100 } };
            var page = refunds ? await c.Payments.ListPendingRefundsAsync(request) : await c.Payments.ListPendingReceiptsAsync(request);
            Assert.Equal(expected.Count, page.TotalCount);
            Assert.Equal(expected.Take(100).Select(x => x.Id), page.Items.Select(x => x.Id));
            var expectedSeed = expected.Where(x => seed.Contains(x.BookingId)).ToArray(); Assert.NotEmpty(expectedSeed);
            // Fetch by booking to remain stable even when other tests have inserted more than one page of private data.
            foreach (var row in expectedSeed)
            {
                var filtered = request with { BookingId = row.BookingId };
                var item = Assert.Single((refunds ? await c.Payments.ListPendingRefundsAsync(filtered) : await c.Payments.ListPendingReceiptsAsync(filtered)).Items);
                Assert.Equal(row.Id, item.Id); Assert.Equal(row.Amount, item.Amount); Assert.Equal(8, item.RowVer.Length);
                Assert.Equal(PaymentStatus.Pending, item.Status); Assert.False(item.IsRecordedByCurrentUser);
                if (refunds) { Assert.Equal(PaymentType.Refund, item.Type); Assert.Equal(row.Account, item.RefundAccount); }
                else
                {
                    Assert.Null(item.RefundAccount);
                    var detail = await c.Payments.GetReceiptForVerificationAsync(item.Id);
                    Assert.Equal(row.BookingId, detail.Booking.Booking.Id); Assert.Equal(detail.Booking.AmountDue, item.AmountDue);
                    Assert.Equal(row.Amount, detail.Receipt.Amount); Assert.True(detail.CanVerify); Assert.True(detail.CanReject);
                    var count = await c.ScalarAsync<byte>("SELECT ReceiptRejectCount FROM dbo.Bookings WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", row.BookingId));
                    Assert.Equal(count, detail.ReceiptRejectCount); Assert.Equal(count + 1, detail.RejectionNumber); Assert.Equal(2, detail.MaxReceiptRejections);
                }
            }
        }
        await Assert.ThrowsAsync<NotFoundException>(() => c.Payments.GetReceiptForVerificationAsync(int.MaxValue));
    }

    [SqlFact]
    public async Task RecorderFlagEvidenceAndInsufficientReceiptAreReadyForVerificationForm()
    {
        var recorder = new M21TestClient(); await recorder.LoginNewAsync(Position.Accountant);
        var d = await M21Data.CreateAsync(recorder); var id = await d.BookingAsync(recorder, 0);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1];
        var receipt = await recorder.Payments.RecordReceiptAsync(new() { BookingId = id, Amount = 100000, Method = PaymentMethod.Cash,
            RowVer = (await recorder.Bookings.GetAsync(id)).Booking.RowVer, EvidenceBytes = png, EvidenceFileName = "evidence.png" });
        var self = await recorder.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        Assert.True(self.Receipt.IsRecordedByCurrentUser); Assert.True(self.IsInsufficient); Assert.False(self.CanVerify);
        Assert.True(Assert.Single((await recorder.Payments.ListPendingReceiptsAsync(new() { BookingId = id })).Items).IsRecordedByCurrentUser);
        var evidence = await recorder.Files.GetEvidenceAsync(id, self.Receipt.EvidenceFileId!.Value);
        Assert.Equal(png, evidence.Content);
        var other = new M21TestClient(); await other.LoginNewAsync(Position.Accountant);
        var read = await other.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        Assert.False(read.Receipt.IsRecordedByCurrentUser); Assert.False(read.CanVerify); Assert.True(read.CanReject);
    }

    [SqlFact]
    public async Task PaymentQueuesFilterAndPageInStableDatabaseOrderAndRejectOtherRoles()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(Position.Accountant);
        var d = await M21Data.CreateAsync(c, 12);
        var ids = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var id = await d.BookingAsync(c, i);
            var receipt = await c.Payments.RecordReceiptAsync(new() { BookingId = id, Amount = 600000, Method = PaymentMethod.Cash,
                PayerName = "Test", RowVer = (await c.Bookings.GetAsync(id)).Booking.RowVer });
            ids.Add(receipt.Receipt.Id);
        }
        var homeName = await c.ScalarAsync<string>("SELECT Name FROM dbo.Homestays WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        var query = new PaymentQueueRequest { Page = new() { PageSize = 10, SearchText = homeName, SortColumn = "RecordedAtUtc" }, Method = PaymentMethod.Cash };
        var first = await c.Payments.ListPendingReceiptsAsync(query);
        var second = await c.Payments.ListPendingReceiptsAsync(query with { Page = query.Page with { PageNumber = 2 } });
        Assert.Equal(12, first.TotalCount); Assert.Equal(12, second.TotalCount);
        Assert.Equal(ids, first.Items.Concat(second.Items).Select(x => x.Id));
        Assert.Empty((await c.Payments.ListPendingReceiptsAsync(query with { Method = PaymentMethod.BankTransfer })).Items);
        Assert.Empty((await c.Payments.ListPendingReceiptsAsync(query with { Page = query.Page with { SearchText = "%_['" } })).Items);
        foreach (var role in new[] { Position.Staff, Position.Manager })
        {
            var forbidden = new M21TestClient(); await forbidden.LoginNewAsync(role);
            await Assert.ThrowsAsync<ForbiddenException>(() => forbidden.Payments.ListPendingReceiptsAsync(new()));
            await Assert.ThrowsAsync<ForbiddenException>(() => forbidden.Payments.ListPendingRefundsAsync(new()));
            await Assert.ThrowsAsync<ForbiddenException>(() => forbidden.Payments.GetReceiptForVerificationAsync(ids[0]));
        }
    }

    [SqlFact]
    public async Task ExistingOwnerConfirmationAndTodayQueuesContainCorrectSeedRows()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var seed = (await SeedBookingIds(c)).ToHashSet();
        var today = await c.ScalarAsync<DateTime>("SELECT CONVERT(datetime2,CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())));");
        foreach (var queue in new[] { BookingQueue.OwnerConfirmation, BookingQueue.ArrivalsToday, BookingQueue.DeparturesToday })
        {
            var status = queue == BookingQueue.OwnerConfirmation ? BookingStatus.Deposited : queue == BookingQueue.ArrivalsToday ? BookingStatus.Confirmed : BookingStatus.CheckedIn;
            var expected = await Rows(c, """
                SELECT b.Id FROM dbo.Bookings b WHERE b.Status=@status AND (@queue=0 OR (@queue=1 AND b.CheckIn=@today) OR (@queue=2 AND b.CheckOut=@today));
                """, r => r.GetInt32("Id"), cmd => { cmd.Parameters.AddEnum("@status", 20, (BookingStatus?)status); cmd.Parameters.AddInt32("@queue", queue == BookingQueue.OwnerConfirmation ? 0 : queue == BookingQueue.ArrivalsToday ? 1 : 2); cmd.Parameters.AddDate("@today", DateOnly.FromDateTime(today)); });
            var page = await c.Bookings.ListAsync(new() { Queue = queue, Page = new() { PageSize = 100 } });
            Assert.Equal(expected.Count, page.TotalCount);
            var actualSeed = page.Items.Where(x => seed.Contains(x.Id)).ToArray(); Assert.NotEmpty(actualSeed);
            Assert.Equal(expected.Where(seed.Contains).Order(), actualSeed.Select(x => x.Id).Order());
            foreach (var b in actualSeed)
            {
                Assert.Equal(status, b.Status); Assert.NotEmpty(b.ContactPhone); Assert.NotEmpty(b.OwnerPhone); Assert.Equal(8, b.RowVer.Length);
                if (queue == BookingQueue.OwnerConfirmation)
                {
                    Assert.NotNull(b.ConfirmDeadlineAtUtc); Assert.Equal(b.DeadlineAtUtc, b.ConfirmDeadlineAtUtc);
                    Assert.Equal((await c.Bookings.GetAsync(b.Id)).Booking.IsConfirmationOverdue, b.IsConfirmationOverdue);
                }
                else Assert.Equal(DateOnly.FromDateTime(today), queue == BookingQueue.ArrivalsToday ? b.CheckIn : b.CheckOut);
            }
        }
    }

    [SqlFact]
    public async Task CancelRequestListDetailAndPreviewMatchSeedAndRespectScope()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var seed = (await SeedBookingIds(c)).ToHashSet();
        var expected = await Rows(c, "SELECT Id,BookingId,QuotedRefundAmount FROM dbo.BookingChangeRequests WHERE RequestType='Cancel';",
            r => (Id: r.GetInt32("Id"), Booking: r.GetInt32("BookingId"), Quote: r.IsDBNull(r.GetOrdinal("QuotedRefundAmount")) ? (decimal?)null : r.GetDecimal("QuotedRefundAmount")));
        var list = await c.Changes.ListAsync(new() { Page = new() { PageSize = 100 } });
        Assert.Equal(expected.Count, list.TotalCount);
        foreach (var row in expected.Where(x => seed.Contains(x.Booking)))
        {
            var detail = await c.Changes.GetAsync(row.Id);
            Assert.Equal(row.Quote, detail.Request.QuotedRefundAmount); Assert.Equal(row.Booking, detail.Booking.Booking.Id);
            Assert.Equal(ChangeRequestType.Cancel, detail.Request.RequestType); Assert.Equal(8, detail.Request.RowVer.Length);
            Assert.Equal(detail.Request.Status == ChangeRequestStatus.Pending, detail.Request.CanReview);
            var page = await c.Changes.ListAsync(new() { BookingId = row.Booking, Status = detail.Request.Status });
            Assert.Contains(page.Items, x => x.Id == row.Id);
        }
        Assert.Contains(list.Items, x => x.Status == ChangeRequestStatus.Pending && seed.Contains(x.BookingId));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Changes.GetAsync(int.MaxValue));
        var outside = new M21TestClient(); await outside.LoginNewAsync(Position.Staff, allAreas: false);
        Assert.Empty((await outside.Changes.ListAsync(new())).Items);
        var pending = expected.First(x => seed.Contains(x.Booking));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Changes.GetAsync(pending.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Changes.PreviewCancelAsync(pending.Booking));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Bookings.ListConfirmationRoomsAsync(pending.Booking));
        foreach (var queue in new[] { BookingQueue.OwnerConfirmation, BookingQueue.ArrivalsToday, BookingQueue.DeparturesToday })
            Assert.Empty((await outside.Bookings.ListAsync(new() { Queue = queue })).Items);
    }

    [SqlFact]
    public async Task RefundPreviewOnSeedUsesServerVietnamDateSavedPolicyAndCalculator()
    {
        var c = new M21TestClient(); await c.LoginNewAsync();
        foreach (var id in await SeedBookingIds(c))
        {
            var detail = await c.Bookings.GetAsync(id);
            if (detail.Booking.Status is not (BookingStatus.Deposited or BookingStatus.Confirmed)) continue;
            var before = await c.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();");
            var preview = await c.Bookings.PreviewRefundAsync(id);
            var after = await c.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();");
            Assert.InRange(preview.ServerNowUtc, before, after); Assert.Equal(DateTimeKind.Utc, preview.ServerNowUtc.Kind);
            Assert.Equal(DateOnly.FromDateTime(preview.ServerNowUtc.AddHours(7)), preview.CancellationDate);
            Assert.Equal(detail.Booking.RowVer, preview.RowVer);
            var expected = RefundCalculator.Calculate(new(detail.Booking.CheckIn, preview.CancellationDate, detail.AmountDue, true, detail.CancellationPolicy)
                { TotalAmount = detail.TotalAmount, CommissionRate = detail.CommissionRate });
            Assert.Equal(expected, preview.Calculation);
            var fromChange = await c.Changes.PreviewCancelAsync(id);
            Assert.Equal(preview.Calculation, fromChange.Calculation);
        }
    }

    [SqlFact]
    public async Task ConfirmationRoomLookupMatchesSeedLedgerAndExcludesCurrentOffAndOccupiedRooms()
    {
        var c = new M21TestClient(); await c.LoginNewAsync();
        var ids = await Rows(c, """
            SELECT b.Id FROM dbo.Bookings b JOIN dbo.Employees e ON e.Id=b.CreatedByEmployeeId
            WHERE e.EmployeeCode LIKE 'NV%' AND b.Status='Deposited';
            """, r => r.GetInt32("Id")); Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            var detail = await c.Bookings.GetAsync(id);
            // Seed has no expired ledger entries for these stays; compare the actual reserved nights directly.
            var expected = await Rows(c, """
                SELECT r.Id FROM dbo.Rooms r JOIN dbo.Bookings b ON b.RoomTypeId=r.RoomTypeId
                WHERE b.Id=@id AND r.Id<>b.RoomId AND r.IsActive=1
                AND NOT EXISTS(SELECT 1 FROM dbo.RoomNights rn WHERE rn.RoomId=r.Id AND rn.[Date]>=b.CheckIn AND rn.[Date]<b.CheckOut)
                ORDER BY r.RoomCode,r.Id;
                """, r => r.GetInt32("Id"), cmd => cmd.Parameters.AddInt32("@id", id));
            var rooms = await c.Bookings.ListConfirmationRoomsAsync(id);
            Assert.Equal(expected, rooms.Select(x => x.Id)); Assert.DoesNotContain(rooms, x => x.Id == detail.Booking.RoomId);
        }
        var d = await M21Data.CreateAsync(c, 5); var booking = await d.BookingAsync(c, 0, BookingStatus.Deposited);
        await d.BookingAsync(c, 1, BookingStatus.Confirmed); await d.BlockAsync(c, 2, RoomBlockReason.Maintenance);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[3]);
        Assert.Equal(d.Rooms[4], Assert.Single(await c.Bookings.ListConfirmationRoomsAsync(booking)).Id);
        var expiredHold = await d.BookingAsync(c, 4, expiredHold: true);
        Assert.Equal(d.Rooms[4], Assert.Single(await c.Bookings.ListConfirmationRoomsAsync(booking)).Id);
        await d.PendingDepositAsync(c, expiredHold);
        Assert.Empty(await c.Bookings.ListConfirmationRoomsAsync(booking));
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.ListConfirmationRoomsAsync(expiredHold));
    }

    [SqlFact]
    public async Task CancelRequestSelfReviewFlagAndStaffCommissionPrivacy()
    {
        var c = new M21TestClient(); var sender = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0, BookingStatus.Deposited);
        await d.PendingDepositAsync(c, id);
        var verifier = await c.Login.CreateIdentityAsync(Position.Accountant);
        await c.MutateAsync("UPDATE dbo.Payments SET Status='Verified',VerifiedByUserId=@user,VerifiedAt=SYSUTCDATETIME() WHERE BookingId=@id;", cmd =>
        { cmd.Parameters.AddInt32("@user", verifier.UserId); cmd.Parameters.AddInt32("@id", id); });
        await c.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,Reason,QuotedRefundAmount,Status,RequestedByUserId,RequestedAt)
            VALUES(@id,'Cancel',N'Yêu cầu riêng',600000,'Pending',@user,SYSUTCDATETIME());
            """, cmd => { cmd.Parameters.AddInt32("@id", id); cmd.Parameters.AddInt32("@user", sender.UserId); });
        var self = Assert.Single((await c.Changes.ListAsync(new() { BookingId = id })).Items);
        Assert.True(self.IsRequestedByCurrentUser); Assert.False(self.CanReview);
        Assert.DoesNotContain(BookingAction.SubmitCancel, (await c.Bookings.GetAsync(id)).AllowedActions);
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var other = (await manager.Changes.GetAsync(self.Id)).Request; Assert.False(other.IsRequestedByCurrentUser); Assert.True(other.CanReview);
        var staff = new M21TestClient(); await staff.LoginNewAsync(Position.Staff, d.AreaId, false);
        Assert.False((await staff.Changes.GetAsync(self.Id)).Request.CanReview);
        var preview = await staff.Bookings.PreviewRefundAsync(id);
        Assert.Equal(600000, preview.Calculation.RefundAmount); Assert.Null(preview.Calculation.CommissionAmount); Assert.Null(preview.Calculation.OwnerPayoutAmount);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Changes.ListAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Changes.GetAsync(self.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.PreviewRefundAsync(id));
    }

    [SqlFact]
    public async Task SeedActionsMatchStateRoleScopeVietnamDatesAndOpenCancelRequest()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync(); var seed = await SeedBookingIds(manager);
        var staff = new M21TestClient(); var employee = await staff.LoginNewAsync(Position.Staff, allAreas: false);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        foreach (var id in seed)
        {
            var detail = await manager.Bookings.GetAsync(id); var b = detail.Booking;
            // Assign exactly the seed booking's area before signing in again, so the real session carries it.
            if (!staff.Current.RequireSession().AreaIds.Contains(b.AreaId))
            { await AddArea(manager, employee, b.AreaId); await staff.Login.LoginAsync(employee); }
            var today = DateOnly.FromDateTime((await manager.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();")).AddHours(7));
            foreach (var client in new[] { manager, staff, accountant })
            {
                var actions = (await client.Bookings.GetAsync(id)).AllowedActions;
                var operates = client != accountant;
                Assert.Equal(operates && b.Status == BookingStatus.Deposited, actions.Contains(BookingAction.ConfirmOnBehalf));
                Assert.Equal(operates && b.Status == BookingStatus.Deposited, actions.Contains(BookingAction.RejectOnBehalf));
                Assert.Equal(operates && b.Status == BookingStatus.Confirmed && today >= b.CheckIn, actions.Contains(BookingAction.CheckIn));
                Assert.Equal(operates && b.Status == BookingStatus.Confirmed && today > b.CheckIn, actions.Contains(BookingAction.MarkNoShow));
                Assert.Equal(operates && b.Status == BookingStatus.CheckedIn, actions.Contains(BookingAction.CheckOut));
                Assert.Equal(client == manager && (b.Status is BookingStatus.Deposited or BookingStatus.Confirmed), actions.Contains(BookingAction.CancelHomestayUnavailable));
                Assert.Equal(operates && (b.Status is BookingStatus.Deposited or BookingStatus.Confirmed) && !detail.ChangeRequests.Any(r => r.Status is ChangeRequestStatus.Pending or ChangeRequestStatus.AwaitingPayment), actions.Contains(BookingAction.SubmitCancel));
            }
        }
        var outside = new M21TestClient(); await outside.LoginNewAsync(Position.Staff, allAreas: false);
        foreach (var id in seed) Assert.Empty((await outside.Bookings.GetAsync(id)).AllowedActions);
    }

    [SqlFact]
    public async Task CheckInAndNoShowActionsHaveExactVietnamDateBoundariesAndEarlyCheckoutIsAllowed()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var today = DateOnly.FromDateTime((await c.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();")).AddHours(7));
        for (var offset = -1; offset <= 1; offset++)
        {
            var date = today.AddDays(offset);
            var id = await d.BookingAsync(c, offset + 1, BookingStatus.Confirmed, checkIn: date, checkOut: date.AddDays(2));
            var actions = (await c.Bookings.GetAsync(id)).AllowedActions;
            Assert.Equal(offset <= 0, actions.Contains(BookingAction.CheckIn)); Assert.Equal(offset < 0, actions.Contains(BookingAction.MarkNoShow));
        }
        var early = await d.BookingAsync(c, 3, BookingStatus.CheckedIn, checkIn: today, checkOut: today.AddDays(2));
        Assert.Contains(BookingAction.CheckOut, (await c.Bookings.GetAsync(early)).AllowedActions);
    }

    [SqlFact]
    public async Task CancelRequestsFilterStatusBookingAndPageWithoutMixingOtherRequestTypes()
    {
        var c = new M21TestClient(); var sender = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 12);
        var ids = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var id = await d.BookingAsync(c, i, BookingStatus.Confirmed);
            ids.Add(id);
            await c.MutateAsync("""
                INSERT dbo.BookingChangeRequests(BookingId,RequestType,Reason,QuotedRefundAmount,Status,RequestedByUserId,RequestedAt)
                VALUES(@id,'Cancel',N'Khách yêu cầu',600000,'Pending',@user,SYSUTCDATETIME());
                """, cmd => { cmd.Parameters.AddInt32("@id", id); cmd.Parameters.AddInt32("@user", sender.UserId); });
        }
        var homeName = await c.ScalarAsync<string>("SELECT Name FROM dbo.Homestays WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        var query = new ChangeRequestSearchRequest { Status = ChangeRequestStatus.Pending,
            Page = new() { PageSize = 10, SortColumn = "RequestedAtUtc", SearchText = homeName } };
        var first = await c.Changes.ListAsync(query);
        var second = await c.Changes.ListAsync(query with { Page = query.Page with { PageNumber = 2 } });
        Assert.Equal(12, first.TotalCount); Assert.Equal(12, second.TotalCount);
        Assert.Equal(ids, first.Items.Concat(second.Items).Select(x => x.BookingId));
        Assert.Empty((await c.Changes.ListAsync(query with { Status = ChangeRequestStatus.Rejected })).Items);
        Assert.Single((await c.Changes.ListAsync(query with { BookingId = ids[0] })).Items);
        Assert.Empty((await c.Changes.ListAsync(query with { Page = query.Page with { SearchText = "%_[" } })).Items);
        // Close one cancel request, then insert a different request type for the same in-scope booking.
        var reviewer = await c.Login.CreateIdentityAsync(Position.Manager);
        await c.MutateAsync("""
            UPDATE dbo.BookingChangeRequests SET Status='Rejected',DecidedByUserId=@reviewer,DecidedAt=SYSUTCDATETIME(),DecisionNote=N'Test'
            WHERE BookingId=@id AND RequestType='Cancel';
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,Reason,Status,RequestedByUserId,RequestedAt)
            SELECT Id,'ChangeDates',DATEADD(day,1,CheckIn),DATEADD(day,1,CheckOut),N'Ngoài phạm vi Đợt 3','Pending',@user,SYSUTCDATETIME()
            FROM dbo.Bookings WHERE Id=@id;
            """, cmd => { cmd.Parameters.AddInt32("@id", ids[0]); cmd.Parameters.AddInt32("@user", sender.UserId);
                cmd.Parameters.AddInt32("@reviewer", reviewer.UserId); });
        Assert.Empty((await c.Changes.ListAsync(query with { BookingId = ids[0] })).Items);
    }

    [SqlFact]
    public async Task AllTwelveRoundThreeWritesHaveRealImplementations()
    {
        var c = new M21TestClient(); await c.LoginNewAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.CancelHomestayUnavailableAsync(new(int.MaxValue, "Không phục vụ được", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Changes.SubmitCancelAsync(new(int.MaxValue, "Khách hủy", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Changes.ApproveAsync(new(int.MaxValue, new byte[8], new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Changes.RejectAsync(new(int.MaxValue, "Không đồng ý", new byte[8], new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.ConfirmOnBehalfAsync(new(int.MaxValue, "Test", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.RejectOnBehalfAsync(new(int.MaxValue, "Test", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.CheckInAsync(new(int.MaxValue, "Test", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.CheckOutAsync(new(int.MaxValue, "Test", new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Bookings.MarkNoShowAsync(new(int.MaxValue, "Test", new byte[8])));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        await Assert.ThrowsAsync<NotFoundException>(() => accountant.Payments.VerifyReceiptAsync(new(int.MaxValue, new byte[8], new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => accountant.Payments.RejectReceiptAsync(new(int.MaxValue, "Test", new byte[8], new byte[8])));
        await Assert.ThrowsAsync<NotFoundException>(() => accountant.Payments.RecordRefundPaidAsync(new() { PaymentId = int.MaxValue, RowVer = new byte[8], BankRef = "TEST", PaidAtUtc = DateTime.UtcNow }));
    }
}
