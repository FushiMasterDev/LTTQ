using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Homestay.DTO.Payments;
using Homestay.DTO.Rooms;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M4.2B: ghi loại phòng, phòng, giá, hợp đồng đến khi bán được và tạo đơn.</summary>
[Collection("M23 SQL")]
public sealed class M42BRoomContractWriteTests
{
    /// <summary>Dữ liệu chuẩn bị sẵn cho các ca kiểm thử M4.2B.</summary>
    private sealed record Fixture(M41TestClient Manager, M41TestClient Staff, int Home, int Area, int Category,
        int Type, int Room, DateOnly Today, int? Contract);
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..20];
    private static byte[] File(byte marker) => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, marker];
    private static async Task<M41TestClient> Client(Position position = Position.Manager, int? area = null, bool all = true)
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(position, area, all); return c;
    }
    private static async Task<byte[]> Version(Fixture f) => (await f.Manager.Homes.GetAsync(f.Home)).Homestay.RowVer;
    private static Task<T> Scalar<T>(Fixture f, string sql, int? id = null) => f.Manager.Base.ScalarAsync<T>(sql, c => c.Parameters.AddInt32("@id", id ?? f.Home));
    private static CreateContractRequest Contract(Fixture f, byte[] version, decimal rate = .12m) => new(f.Home, Unique("HD"), f.Today, f.Today.AddYears(1), rate, version);
    private static async Task<Fixture> Draft(bool contract = true)
    {
        var m = await Client(); var catalog = m.Base.Login.Catalog;
        var area = await catalog.CreateAreaAsync(new() { Name = Unique("Vùng "), Province = "M42B", AreaGroup = AreaGroup.InnerCity });
        var category = await catalog.CreateItemAsync(CatalogKind.RoomCategory, new(Unique("Danh mục ")));
        var s = await Client(Position.Staff, area, false);
        var owner = await s.Owners.CreateAsync(new() { FullName = "Chủ M42B", Phone = M14TestClient.Phone(), BankName = "Ngân hàng", BankAccountNo = "123456789012345", BankAccountName = "CHU M42B" });
        owner = await m.Owners.ChangeStatusAsync(new(owner.Id, OwnerStatus.Approved, null, (await m.Owners.GetAsync(owner.Id)).RowVer));
        var h = await s.Homes.CreateDraftAsync(new() { OwnerId = owner.Id, AreaId = area, Name = Unique("Nhà "), Address = "Địa chỉ M42B", ContactPhone = "0901234567", Description = "Mô tả M42B" });
        var type = await s.Rooms.CreateTypeAsync(new(h.Homestay.Id, category, " Loại phòng ", 4, 25, " Mô tả ", h.Homestay.RowVer));
        var room = await s.Rooms.CreateRoomAsync(new(type.Id, " A1 ", " Ghi chú ", type.RowVer));
        var today = (await s.Rooms.ListPricesAsync(h.Homestay.Id)).Today;
        var prices = await s.Rooms.AddPriceAsync(new(type.Id, today, 1000000, 1200000, 1500000, room.RowVer));
        var d = await s.Homes.SetCancellationPolicyAsync(new(h.Homestay.Id, new() { Name = "Hoàn tiền M42B", Rules = [new(0, 100)] }, Assert.Single(prices.Versions).RowVer));
        var f = new Fixture(m, s, h.Homestay.Id, area, category, type.Id, room.Id, today, null);
        if (contract)
        {
            var c = await m.Contracts.CreateAsync(Contract(f, d.Homestay.RowVer));
            f = f with { Contract = c.Id };
        }
        return f;
    }
    private static async Task<Fixture> Active()
    {
        var f = await Draft(); var d = await f.Staff.Homes.GetAsync(f.Home);
        d = await f.Staff.Homes.AddImageAsync(new(f.Home, File(1), "cover.png", null, null, 0, true, d.Homestay.RowVer));
        d = await f.Staff.Homes.AddDocumentAsync(new(f.Home, DocumentType.BusinessLicense, File(2), "license.png", null, d.Homestay.RowVer));
        d = await f.Staff.Homes.AddDocumentAsync(new(f.Home, DocumentType.FireSafety, File(3), "fire.png", null, d.Homestay.RowVer));
        d = await f.Staff.Homes.SubmitAsync(new(f.Home, d.Homestay.RowVer));
        // Another manager, distinct from the contract signer and original draft creator.
        var reviewer = await Client(); d = await reviewer.Homes.ApproveAsync(new(f.Home, d.Homestay.RowVer));
        Assert.Equal(HomestayStatus.Active, d.Homestay.Status); return f;
    }
    private static AvailabilitySearchRequest Search(Fixture f, DateOnly? date = null) => new() { AreaId = f.Area, CheckIn = date ?? f.Today.AddDays(30), CheckOut = (date ?? f.Today.AddDays(30)).AddDays(2), Guests = 2 };
    private static CreateBookingRequest Booking(Fixture f, DateOnly? date = null) => new()
    {
        QuickCustomer = new() { FullName = "Khách M42B", Phone = M14TestClient.Phone() }, RoomTypeId = f.Type, RoomId = f.Room,
        CheckIn = date ?? f.Today.AddDays(30), CheckOut = (date ?? f.Today.AddDays(30)).AddDays(2), Guests = 2,
        ContactName = "Khách M42B", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone
    };
    private static Task<RoomBlockItem> Block(Fixture f, byte[] version, DateOnly? date = null) =>
        f.Manager.Rooms.BlockAsync(new(f.Room, date ?? f.Today.AddDays(30), (date ?? f.Today.AddDays(30)).AddDays(2), RoomBlockReason.Maintenance, " Bảo trì ", version));
    private static async Task<Exception?[]> Race(params Func<Task>[] actions)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(async action => { await gate.Task; return await Record.ExceptionAsync(action); }).ToArray();
        gate.SetResult(); return await Task.WhenAll(tasks);
    }
    [SqlFact]
    public async Task Flow910EntirelyThroughBllSellsAndCreatesBookingAtQuotedThreeLevelPrices()
    {
        var f = await Active();
        var types = await f.Manager.Rooms.ListTypesAsync(f.Home); var room = Assert.Single(await f.Manager.Rooms.ListRoomsAsync(f.Home));
        Assert.Equal("Loại phòng", Assert.Single(types).Name); Assert.Equal("A1", room.RoomCode); Assert.True(room.IsActive);
        Assert.Equal(3, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomTypePrices WHERE RoomTypeId=@id;", f.Type));
        Assert.Contains(await f.Staff.Base.Availability.SearchAsync(Search(f)), r => r.HomestayId == f.Home && r.AvailableRooms == 1);
        var quote = await f.Staff.Base.Availability.QuoteAsync(new(f.Type, Search(f).CheckIn, Search(f).CheckOut, 2));
        var booking = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        Assert.Equal(quote.Pricing.Nights, booking.Nights); Assert.Equal(quote.Pricing.TotalAmount, booking.TotalAmount);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", booking.Booking.Id));
        var audit = (await f.Manager.Homes.GetAsync(f.Home)).Audit;
        foreach (var action in new[] { "Room.CreateType", "Room.Create", "Price.Add", "Contract.Create", "Homestay.Approve" }) Assert.Contains(audit, a => a.ActionCode == action);
    }
    [SqlFact]
    public async Task PriceEffectiveVersionsAreImmutableFutureVersionsChangeAndOldBookingKeepsSnapshot()
    {
        var f = await Active(); var version = await Version(f);
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.UpdatePriceAsync(new(f.Type, f.Today, 1, 2, 3, version)));
        Assert.Contains("đã hiệu lực", error.Message); Assert.Equal(version, await Version(f));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.AddPriceAsync(new(f.Type, f.Today.AddDays(-1), 1, 2, 3, version)));
        var old = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        var future = f.Today.AddDays(10);
        var prices = await f.Manager.Rooms.AddPriceAsync(new(f.Type, future, 2000000, 2000000, 2000000, version));
        var p = prices.Versions.Single(p => p.EffectiveFrom == future); Assert.False(p.HasTakenEffect);
        prices = await f.Manager.Rooms.UpdatePriceAsync(new(f.Type, future, 3000000, 3000000, 3000000, p.RowVer));
        Assert.Equal(3000000m, prices.Versions.Single(p => p.EffectiveFrom == future).WeekdayPrice);
        var next = await f.Staff.Base.Bookings.CreateAsync(Booking(f, f.Today.AddDays(40)));
        Assert.All(next.Nights, n => Assert.Equal(3000000m, n.Price)); Assert.Equal(6000000m, next.TotalAmount);
        var reread = await f.Staff.Base.Bookings.GetAsync(old.Booking.Id);
        Assert.Equal(old.TotalAmount, reread.TotalAmount); Assert.Equal(old.Nights, reread.Nights);
    }
    [SqlFact]
    public async Task LastRoomAndTypeCannotBeDisabledOnActiveAndStaffCannotEditActive()
    {
        var f = await Active(); var v = await Version(f); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.SetRoomActiveAsync(new(f.Room, false, v)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.SetTypeActiveAsync(new(f.Type, false, v)));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Rooms.UpdateRoomAsync(new(f.Room, "A2", null, v)));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Rooms.UpdateTypeAsync(new(f.Type, f.Category, "Đổi tên", 4, null, null, v)));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Rooms.AddPriceAsync(new(f.Type, f.Today.AddDays(1), 1, 2, 3, v)));
        Assert.Equal(v, await Version(f));
        var second = await f.Manager.Rooms.CreateRoomAsync(new(f.Type, "A2", null, v));
        var off = await f.Manager.Rooms.SetRoomActiveAsync(new(f.Room, false, second.RowVer)); Assert.False(off.IsActive);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
        Assert.Equal(b.Nights, (await f.Manager.Base.Bookings.GetAsync(b.Booking.Id)).Nights);
    }
    [SqlFact]
    public async Task RoomAndTypeEditsNormalizePreserveTypeAndDraftCanTurnOffThenOn()
    {
        var f = await Draft(); var v = await Version(f);
        var type = await f.Staff.Rooms.UpdateTypeAsync(new(f.Type, f.Category, " Loại mới ", 5, 30, " Mới ", v));
        Assert.Equal("Loại mới", type.Name); Assert.Equal(5, type.MaxGuests);
        var room = await f.Staff.Rooms.UpdateRoomAsync(new(f.Room, " B1 ", " Note ", type.RowVer));
        Assert.Equal(f.Type, room.RoomTypeId); Assert.Equal("B1", room.RoomCode); Assert.Equal("Note", room.Note);
        room = await f.Staff.Rooms.SetRoomActiveAsync(new(f.Room, false, room.RowVer)); Assert.False(room.IsActive);
        room = await f.Staff.Rooms.SetRoomActiveAsync(new(f.Room, true, room.RowVer)); Assert.True(room.IsActive);
        type = await f.Staff.Rooms.SetTypeActiveAsync(new(f.Type, false, room.RowVer)); Assert.False(type.IsActive);
        type = await f.Staff.Rooms.SetTypeActiveAsync(new(f.Type, true, type.RowVer)); Assert.True(type.IsActive);
    }
    [SqlFact]
    public async Task BlockDetectsBookingCodeAndOtherBlocksCalendarSearchAndCreateAgreeThenUnblockReleases()
    {
        var f = await Active(); var v = await Version(f); var booking = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        var e = await Assert.ThrowsAsync<BusinessException>(() => Block(f, v)); Assert.Contains(booking.Booking.Code, e.Message);
        await f.Staff.Base.Bookings.CancelUnpaidAsync(new(booking.Booking.Id, "Hủy thử", booking.Booking.RowVer));
        var block = await Block(f, v); Assert.Equal("Bảo trì", block.Note);
        e = await Assert.ThrowsAsync<BusinessException>(() => Block(f, block.RowVer)); Assert.Contains("đã bị khóa", e.Message);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Rooms.BlockAsync(new(f.Room, block.StartDate, block.EndDate, RoomBlockReason.Maintenance, null, block.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Rooms.UnblockAsync(new(block.Id, "Mở", block.RowVer)));
        Assert.DoesNotContain(await f.Staff.Base.Availability.SearchAsync(Search(f)), x => x.HomestayId == f.Home && x.AvailableRooms > 0);
        var calendar = await f.Staff.Rooms.GetCalendarAsync(new(f.Home, null, block.StartDate));
        Assert.All(calendar.Rooms.Single(r => r.RoomId == f.Room).Cells.Take(2), n => { Assert.Equal(block.Id, n.RoomBlockId); Assert.Equal(RoomCalendarState.MaintenanceBlock, n.State); });
        await Assert.ThrowsAsync<BusinessException>(() => f.Staff.Base.Bookings.CreateAsync(Booking(f)));
        var released = await f.Manager.Rooms.UnblockAsync(new(block.Id, "  Xong bảo trì  ", block.RowVer));
        Assert.NotEqual(block.RowVer, released.RowVer);
        Assert.Equal(0, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE RoomBlockId=@id;", block.Id));
        Assert.Contains(await f.Staff.Base.Availability.SearchAsync(Search(f)), x => x.HomestayId == f.Home && x.AvailableRooms == 1);
        Assert.Equal("Xong bảo trì", (await f.Manager.Homes.GetAsync(f.Home)).Audit.Last().Note);
        await f.Staff.Base.Bookings.CreateAsync(Booking(f));
    }
    [SqlFact]
    public async Task ExpiredHoldIsReleasedInBlockTransactionWithSystemHistoryAndAudit()
    {
        var f = await Active(); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        await f.Manager.Base.MutateAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id;", c => c.Parameters.AddInt32("@id", b.Booking.Id));
        var block = await Block(f, await Version(f));
        Assert.Equal(BookingStatus.Expired, (await f.Manager.Base.Bookings.GetAsync(b.Booking.Id)).Booking.Status);
        Assert.Equal(0, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE RoomBlockId=@id;", block.Id));
        Assert.Equal(1, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='Expired' AND ActorType='System';", b.Booking.Id));
    }
    [SqlFact]
    public async Task PendingReceiptKeepsExpiredHoldAndBlockMessageContainsBookingCode()
    {
        var f = await Active(); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        await f.Staff.Base.Payments.RecordReceiptAsync(new() { BookingId = b.Booking.Id, RowVer = b.Booking.RowVer, Amount = b.AmountDue, Method = PaymentMethod.Cash });
        await f.Manager.Base.MutateAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id;", c => c.Parameters.AddInt32("@id", b.Booking.Id));
        var v = await Version(f);
        var e = await Assert.ThrowsAsync<BusinessException>(() => Block(f, v)); Assert.Contains(b.Booking.Code, e.Message);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
    }
    [SqlFact]
    public async Task BlockAndBookingRaceHasExactlyOneWinnerAndNoDuplicateNight()
    {
        var f = await Active(); var version = await Version(f);
        var result = await Race(() => Block(f, version), () => f.Staff.Base.Bookings.CreateAsync(Booking(f)));
        Assert.Single(result, e => e is null); Assert.IsType<BusinessException>(Assert.Single(result, e => e is not null));
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE RoomId=@id;", f.Room));
        Assert.Equal(0, await Scalar<int>(f, "SELECT COUNT(*) FROM (SELECT RoomId,[Date] FROM dbo.RoomNights WHERE RoomId=@id GROUP BY RoomId,[Date] HAVING COUNT(*)>1) d;", f.Room));
        Assert.Equal(1, await Scalar<int>(f, "SELECT (SELECT COUNT(*) FROM dbo.RoomBlocks WHERE RoomId=@id)+(SELECT COUNT(*) FROM dbo.Bookings WHERE RoomId=@id);", f.Room));
    }
    [SqlFact]
    public async Task ContractCommissionDatesAndSecondActiveAreRejectedAndActiveExpiredDateStillOwnsUnique()
    {
        var f = await Draft(false); var v = await Version(f);
        foreach (var rate in new[] { .0999m, .1501m, .12001m }) await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, v, rate)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, v) with { EndDate = f.Today }));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, v) with { StartDate = f.Today.AddDays(1) }));
        var c = await f.Manager.Contracts.CreateAsync(Contract(f, v, .10m)); Assert.Equal(ContractStatus.Active, c.Status);
        var e = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, c.RowVer))); Assert.Equal(ConstraintMessages.GetMessage("UX_Contracts_OneActive"), e.Message);
        await f.Manager.Base.MutateAsync("UPDATE dbo.Contracts SET StartDate=DATEADD(day,-2,@today),EndDate=DATEADD(day,-1,@today) WHERE Id=@id;", p => { p.Parameters.AddDate("@today", f.Today); p.Parameters.AddInt32("@id", c.Id); });
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, c.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.ActivateAsync(new(c.Id, c.RowVer)));
    }
    [SqlFact]
    public async Task ContractUsesConfiguredCommissionBoundsAndRejectsInvertedBounds()
    {
        var f = await Draft(false); var v = await Version(f);
        var min = await f.Manager.Base.ScalarAsync<string>("SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey='Contract.MinCommission';");
        var max = await f.Manager.Base.ScalarAsync<string>("SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey='Contract.MaxCommission';");
        try
        {
            await f.Manager.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue=CASE SettingKey WHEN 'Contract.MinCommission' THEN N'0.11' ELSE N'0.14' END WHERE SettingKey IN ('Contract.MinCommission','Contract.MaxCommission');");
            await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, v, .10m)));
            var c = await f.Manager.Contracts.CreateAsync(Contract(f, v, .14m));
            Assert.Equal(.14m, c.CommissionRate); c = await f.Manager.Contracts.TerminateAsync(new(c.Id, "Kết thúc", c.RowVer));
            await f.Manager.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue=N'0.15' WHERE SettingKey='Contract.MinCommission';");
            var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.CreateAsync(Contract(f, c.RowVer)));
            Assert.Contains("cấu hình không hợp lệ", error.Message);
        }
        finally
        {
            await f.Manager.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue=CASE SettingKey WHEN 'Contract.MinCommission' THEN @min ELSE @max END WHERE SettingKey IN ('Contract.MinCommission','Contract.MaxCommission');", p =>
            { p.Parameters.AddNVarChar("@min", 500, min); p.Parameters.AddNVarChar("@max", 500, max); });
        }
    }
    [SqlFact]
    public async Task TwoManagersCreatingContractHaveExactlyOneActiveAndOneConcurrencyFailure()
    {
        var f = await Draft(false); var other = await Client(); var v = await Version(f);
        var results = await Race(() => f.Manager.Contracts.CreateAsync(Contract(f, v)), () => other.Contracts.CreateAsync(Contract(f, v)));
        Assert.Single(results, e => e is null); Assert.IsType<ConcurrencyException>(Assert.Single(results, e => e is not null));
        Assert.Equal(1, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.Contracts WHERE HomestayId=@id AND Status='Active';"));
        Assert.Equal(1, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Contract.Create';"));
    }
    [SqlFact]
    public async Task ActivateRevalidatesExtendOnlyGrowsAndTerminateRecordsHistoryWithoutDeleting()
    {
        var f = await Draft(); var c = await f.Manager.Contracts.GetAsync(f.Contract!.Value); var v = c.RowVer;
        c = await f.Manager.Contracts.ActivateAsync(new(c.Id, c.RowVer)); Assert.NotEqual(v, c.RowVer); Assert.Equal(ContractStatus.Active, c.Status);
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.ExtendAsync(new(c.Id, c.EndDate, c.RowVer)));
        var end = c.EndDate.AddDays(30); c = await f.Manager.Contracts.ExtendAsync(new(c.Id, end, c.RowVer)); Assert.Equal(end, c.EndDate); Assert.Equal(.12m, c.CommissionRate);
        c = await f.Manager.Contracts.TerminateAsync(new(c.Id, "  Kết thúc hợp tác  ", c.RowVer));
        Assert.Equal(ContractStatus.Terminated, c.Status); Assert.Equal("Kết thúc hợp tác", c.TerminateReason); Assert.NotNull(c.TerminatedAtUtc);
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.ActivateAsync(new(c.Id, c.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.ExtendAsync(new(c.Id, end.AddDays(1), c.RowVer)));
        var next = await f.Manager.Contracts.CreateAsync(Contract(f, c.RowVer, .15m)); Assert.NotEqual(c.Id, next.Id);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.Contracts WHERE HomestayId=@id;"));
    }
    [SqlFact]
    public async Task TerminateRejectsOccupyingBookingButAllowsExpiredHoldAndCompleted()
    {
        var f = await Active(); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f)); var c = await f.Manager.Contracts.GetAsync(f.Contract!.Value);
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.TerminateAsync(new(c.Id, "Chấm dứt", c.RowVer)));
        await f.Manager.Base.MutateAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id;", p => p.Parameters.AddInt32("@id", b.Booking.Id));
        c = await f.Manager.Contracts.TerminateAsync(new(c.Id, "Không còn giữ", c.RowVer)); Assert.Equal(ContractStatus.Terminated, c.Status);
        // Completed keeps historical ledger, but is excluded by the existing CanEnd booking predicate.
        f = await Active(); b = await f.Staff.Base.Bookings.CreateAsync(Booking(f)); c = await f.Manager.Contracts.GetAsync(f.Contract!.Value);
        await f.Manager.Base.MutateAsync("UPDATE dbo.Bookings SET Status='Completed',DepositedAt=SYSUTCDATETIME(),ConfirmedAt=SYSUTCDATETIME(),CheckedInAt=SYSUTCDATETIME(),CompletedAt=SYSUTCDATETIME() WHERE Id=@id;", p => p.Parameters.AddInt32("@id", b.Booking.Id));
        c = await f.Manager.Contracts.TerminateAsync(new(c.Id, "Đã ở xong", c.RowVer)); Assert.Equal(ContractStatus.Terminated, c.Status);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
    }
    [SqlFact]
    public async Task EveryRoomAndContractWriterRejectsStaleParentVersionWithoutAuditOrMutation()
    {
        var f = await Draft(); var v = await Version(f);
        var future = await f.Manager.Rooms.AddPriceAsync(new(f.Type, f.Today.AddDays(1), 1, 2, 3, v));
        var fresh = future.Versions[0].RowVer;
        var block = await Block(f, fresh); var stale = block.RowVer;
        await f.Manager.Rooms.UpdateRoomAsync(new(f.Room, "A1", null, stale));
        var before = await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id);");
        Func<Task>[] calls = [
            () => f.Manager.Rooms.CreateTypeAsync(new(f.Home, f.Category, "Autre", 2, null, null, stale)),
            () => f.Manager.Rooms.UpdateTypeAsync(new(f.Type, f.Category, "Autre", 2, null, null, stale)),
            () => f.Manager.Rooms.SetTypeActiveAsync(new(f.Type, false, stale)),
            () => f.Manager.Rooms.CreateRoomAsync(new(f.Type, "B", null, stale)),
            () => f.Manager.Rooms.UpdateRoomAsync(new(f.Room, "B", null, stale)),
            () => f.Manager.Rooms.SetRoomActiveAsync(new(f.Room, false, stale)),
            () => f.Manager.Rooms.AddPriceAsync(new(f.Type, f.Today.AddDays(2), 1, 2, 3, stale)),
            () => f.Manager.Rooms.UpdatePriceAsync(new(f.Type, f.Today.AddDays(1), 1, 2, 3, stale)),
            () => Block(f, stale, f.Today.AddDays(40)),
            () => f.Manager.Rooms.UnblockAsync(new(block.Id, "Mở", stale)),
            () => f.Manager.Contracts.CreateAsync(Contract(f, stale)),
            () => f.Manager.Contracts.ActivateAsync(new(f.Contract!.Value, stale)),
            () => f.Manager.Contracts.ExtendAsync(new(f.Contract!.Value, f.Today.AddYears(2), stale)),
            () => f.Manager.Contracts.TerminateAsync(new(f.Contract!.Value, "Chấm dứt", stale)) ];
        Assert.Equal(14, calls.Length); foreach (var call in calls) await Assert.ThrowsAsync<ConcurrencyException>(call);
        Assert.Equal(before, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id);"));
    }
    [SqlFact]
    public async Task AccountantCannotCallAnyRoomOrContractWriteAndStaffCannotCallContracts()
    {
        var kt = await Client(Position.Accountant); var f = await Draft(); var v = await Version(f);
        Func<Task>[] calls = [
            () => kt.Rooms.CreateTypeAsync(null!), () => kt.Rooms.UpdateTypeAsync(null!), () => kt.Rooms.SetTypeActiveAsync(null!),
            () => kt.Rooms.CreateRoomAsync(null!), () => kt.Rooms.UpdateRoomAsync(null!), () => kt.Rooms.SetRoomActiveAsync(null!),
            () => kt.Rooms.AddPriceAsync(null!), () => kt.Rooms.UpdatePriceAsync(null!), () => kt.Rooms.BlockAsync(null!), () => kt.Rooms.UnblockAsync(null!),
            () => kt.Contracts.CreateAsync(null!), () => kt.Contracts.ActivateAsync(null!), () => kt.Contracts.ExtendAsync(null!), () => kt.Contracts.TerminateAsync(null!),
            () => f.Staff.Contracts.CreateAsync(Contract(f, v)), () => f.Staff.Contracts.ActivateAsync(new(f.Contract!.Value, v)),
            () => f.Staff.Contracts.ExtendAsync(new(f.Contract!.Value, f.Today.AddYears(2), v)), () => f.Staff.Contracts.TerminateAsync(new(f.Contract!.Value, "Dừng", v)) ];
        foreach (var call in calls) await Assert.ThrowsAsync<ForbiddenException>(call);
    }
    [SqlFact]
    public async Task CatalogAndUniqueConstraintsRejectWritesAndPriceGroupRollsBackTogether()
    {
        var f = await Draft(); var v = await Version(f);
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.CreateRoomAsync(new(f.Type, "A1", null, v)));
        Assert.Equal(ConstraintMessages.GetMessage("UQ_Rooms_RoomTypeId_RoomCode"), duplicate.Message);
        duplicate = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.CreateTypeAsync(new(f.Home, f.Category, "Loại phòng", 2, null, null, v)));
        Assert.Equal(ConstraintMessages.GetMessage("UQ_RoomTypes_HomestayId_Name"), duplicate.Message);
        duplicate = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.AddPriceAsync(new(f.Type, f.Today, 9, 9, 9, v)));
        Assert.Equal(ConstraintMessages.GetMessage("UQ_RoomTypePrices_RoomTypeId_DayType_EffectiveFrom"), duplicate.Message);
        var inactive = await f.Manager.Base.Login.Catalog.CreateItemAsync(CatalogKind.RoomCategory, new(Unique("Tắt ")));
        await f.Manager.Base.Login.Catalog.SetActiveAsync(CatalogKind.RoomCategory, inactive, true, false);
        foreach (var id in new[] { inactive, int.MaxValue })
        {
            await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.CreateTypeAsync(new(f.Home, id, "Mới", 2, null, null, v)));
            await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.UpdateTypeAsync(new(f.Type, id, "Mới", 2, null, null, v)));
        }
        Assert.Equal(v, await Version(f));
        // Incomplete legacy version: two UPDATEs must roll back when the third level is absent.
        var future = f.Today.AddDays(1);
        await f.Manager.Base.MutateAsync("INSERT dbo.RoomTypePrices(RoomTypeId,DayType,Price,EffectiveFrom,CreatedByUserId) VALUES(@type,'Weekday',11,@from,@user),(@type,'Weekend',12,@from,@user);", p =>
        { p.Parameters.AddInt32("@type", f.Type); p.Parameters.AddDate("@from", future); p.Parameters.AddInt32("@user", f.Manager.Base.Current.RequireSession().UserId); });
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.UpdatePriceAsync(new(f.Type, future, 21, 22, 23, v)));
        var prices = (await f.Manager.Rooms.ListPricesAsync(f.Home)).Versions.Single(p => p.EffectiveFrom == future);
        Assert.Equal(11m, prices.WeekdayPrice); Assert.Equal(12m, prices.WeekendPrice); Assert.Null(prices.HolidayPrice); Assert.Equal(v, prices.RowVer);
    }
    [SqlFact]
    public async Task LedgerPrimaryKeyFailureRollsBackBlockExpiryParentVersionAndAuditTogether()
    {
        var f = await Active();
        var expired = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        var cancelled = await f.Staff.Base.Bookings.CreateAsync(Booking(f, f.Today.AddDays(40)));
        await f.Staff.Base.Bookings.CancelUnpaidAsync(new(cancelled.Booking.Id, "Hủy", cancelled.Booking.RowVer));
        await f.Manager.Base.MutateAsync("""
            UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@expired;
            INSERT dbo.RoomNights(RoomId,[Date],BookingId) VALUES(@room,@date,@cancelled);
            """, p => { p.Parameters.AddInt32("@expired", expired.Booking.Id); p.Parameters.AddInt32("@room", f.Room); p.Parameters.AddDate("@date", f.Today.AddDays(40)); p.Parameters.AddInt32("@cancelled", cancelled.Booking.Id); });
        var v = await Version(f);
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Rooms.BlockAsync(new(f.Room, f.Today.AddDays(30), f.Today.AddDays(42), RoomBlockReason.Maintenance, null, v)));
        Assert.Equal(ConstraintMessages.GetMessage("PK_RoomNights"), error.Message);
        Assert.Equal(v, await Version(f)); Assert.Equal(0, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomBlocks WHERE RoomId=@id;", f.Room));
        Assert.Equal(BookingStatus.AwaitingPayment, (await f.Manager.Base.Bookings.GetAsync(expired.Booking.Id)).Booking.Status);
        Assert.Equal(2, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", expired.Booking.Id));
        Assert.Equal(0, await Scalar<int>(f, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.Expire';", expired.Booking.Id));
    }
    [SqlFact]
    public async Task BlockAndTerminateIncludeChangeRequestHoldsUsingTheSharedOccupancyPredicate()
    {
        var f = await Active(); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        var target = f.Today.AddDays(40);
        await f.Manager.Base.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,NewRoomId,Reason,Status,RequestedByUserId,RequestedAt)
            VALUES(@booking,'ChangeDates',@start,DATEADD(day,2,@start),@room,N'Giữ ngày mới','Pending',@user,SYSUTCDATETIME());
            DECLARE @request int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.RoomNights(RoomId,[Date],ChangeRequestId) VALUES(@room,@start,@request),(@room,DATEADD(day,1,@start),@request);
            """, p => { p.Parameters.AddInt32("@booking", b.Booking.Id); p.Parameters.AddInt32("@room", f.Room); p.Parameters.AddDate("@start", target); p.Parameters.AddInt32("@user", f.Staff.Base.Current.RequireSession().UserId); });
        var v = await Version(f); var error = await Assert.ThrowsAsync<BusinessException>(() => Block(f, v, target)); Assert.Contains(b.Booking.Code, error.Message);
        // Only the request's ledger remains so termination cannot be blocked merely by the original stay.
        await f.Manager.Base.MutateAsync("DELETE dbo.RoomNights WHERE BookingId=@id;", p => p.Parameters.AddInt32("@id", b.Booking.Id));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Contracts.TerminateAsync(new(f.Contract!.Value, "Còn yêu cầu đổi", v)));
    }
    [SqlFact]
    public async Task ParentScopeAndRevokedRealSessionAreCheckedInsideWrites()
    {
        var f = await Draft(); var outside = await Client(Position.Manager, all: false); var v = await Version(f);
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.UpdateRoomAsync(new(f.Room, "Ngoài", null, v)));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.CreateTypeAsync(new(f.Home, f.Category, "Ngoài", 1, null, null, v)));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Contracts.ActivateAsync(new(f.Contract!.Value, v)));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.BlockAsync(new(f.Room, f.Today, f.Today.AddDays(1), RoomBlockReason.Maintenance, null, v)));
        await f.Manager.Base.MutateAsync("UPDATE dbo.UserSessions SET EndedAt=SYSUTCDATETIME(),EndReason='ForcedByManager' WHERE UserId=@id AND EndedAt IS NULL;", p => p.Parameters.AddInt32("@id", f.Staff.Base.Current.RequireSession().UserId));
        await Assert.ThrowsAsync<SessionExpiredException>(() => f.Staff.Rooms.UpdateRoomAsync(new(f.Room, "Mất phiên", null, v)));
        Assert.Equal(v, await Version(f));
    }
    private static async Task<(Fixture F, M41TestClient Accountant, PaymentQueueItem Refund)> Refund()
    {
        var f = await Active(); var b = await f.Staff.Base.Bookings.CreateAsync(Booking(f));
        var receipt = await f.Staff.Base.Payments.RecordReceiptAsync(new() { BookingId = b.Booking.Id, RowVer = b.Booking.RowVer, Amount = b.AmountDue, Method = PaymentMethod.Cash });
        var kt = await Client(Position.Accountant); var review = await kt.Base.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        b = await kt.Base.Payments.VerifyReceiptAsync(new(review.Receipt.Id, review.Receipt.RowVer, review.Booking.Booking.RowVer));
        await f.Manager.Base.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Homestay không phục vụ được", b.Booking.RowVer));
        return (f, kt, Assert.Single((await kt.Base.Payments.ListPendingRefundsAsync(new() { BookingId = b.Booking.Id })).Items));
    }
    [SqlFact]
    public async Task RefundAccountPersistsMaskedAuditAndQueueThenPaysWithReturnedVersion()
    {
        var (f, kt, p) = await Refund(); const string account = "78901234567890123456";
        var result = await kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, "  " + account + "  ", "  KHACH M42B  ", p.RowVer));
        Assert.Equal("KHACH M42B", result.PayeeName); Assert.Equal(ReceiptRules.MaskAccount(account), result.MaskedPayeeAccount); Assert.NotEqual(p.RowVer, result.RowVer);
        var queue = Assert.Single((await kt.Base.Payments.ListPendingRefundsAsync(new() { BookingId = p.BookingId })).Items);
        Assert.Equal(account, queue.RefundAccount); Assert.EndsWith("3456", queue.MaskedPayerAccount); Assert.DoesNotContain(account, queue.MaskedPayerAccount!);
        Assert.Equal(p.RecordedByUserId, queue.RecordedByUserId); Assert.Equal(p.Amount, queue.Amount); Assert.Equal(PaymentStatus.Pending, queue.Status);
        var json = await Scalar<string>(f, "SELECT CONCAT(OldValues,NewValues,Note) FROM dbo.AuditLogs WHERE TableName='Payments' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Payment.UpdateRefundAccount';", p.Id);
        Assert.DoesNotContain(account, json); Assert.DoesNotContain("KHACH M42B", json); Assert.Contains(ReceiptRules.MaskAccount(account)!, json);
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, "1234567890", null, p.RowVer)));
        var now = await new DbClock(kt.Base.Factory).GetUtcNowAsync(CancellationToken.None);
        var paid = await kt.Base.Payments.RecordRefundPaidAsync(new() { PaymentId = p.Id, RowVer = result.RowVer, BankRef = Unique("REF"), PaidAtUtc = now });
        Assert.Equal(PaymentStatus.Verified, paid.Status); Assert.Equal(account, paid.RefundAccount);
        await Assert.ThrowsAsync<BusinessException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, account, null, paid.RowVer)));
    }
    [SqlFact]
    public async Task RefundAccountRejectsOtherRolesInvalidAsciiLengthsWrongTypeAndDoesNotLeakOldOrNewAccount()
    {
        var (f, kt, p) = await Refund();
        foreach (var client in new[] { f.Staff, f.Manager }) await Assert.ThrowsAsync<ForbiddenException>(() => client.Base.Payments.UpdateRefundAccountAsync(new(p.Id, "1234567890", null, p.RowVer)));
        foreach (var account in new[] { " ", new string('1', 31), "１２３", "12\t34", "••••1234" })
            await Assert.ThrowsAsync<BusinessException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, account, null, p.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, "1234567890", new string('A', 101), p.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(int.MaxValue, "1234567890", null, p.RowVer)));
        var receipt = (await f.Manager.Base.Bookings.GetAsync(p.BookingId)).Payments.Single(p => p.Type == PaymentType.Deposit);
        var rv = await Scalar<byte[]>(f, "SELECT RowVer FROM dbo.Payments WHERE Id=@id;", receipt.Id);
        await Assert.ThrowsAsync<BusinessException>(() => kt.Base.Payments.UpdateRefundAccountAsync(new(receipt.Id, "1234567890", null, rv)));
        const string oldAccount = "123456789012345"; const string nextAccount = "234567890123456";
        var first = await kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, oldAccount, null, p.RowVer));
        await kt.Base.Payments.UpdateRefundAccountAsync(new(p.Id, nextAccount, oldAccount, first.RowVer));
        var logs = await Scalar<string>(f, "SELECT STRING_AGG(CONVERT(nvarchar(max),CONCAT(OldValues,NewValues,Note)),N' ') FROM dbo.AuditLogs WHERE TableName='Payments' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Payment.UpdateRefundAccount';", p.Id);
        Assert.DoesNotContain(oldAccount, logs); Assert.DoesNotContain(nextAccount, logs);
    }
}
