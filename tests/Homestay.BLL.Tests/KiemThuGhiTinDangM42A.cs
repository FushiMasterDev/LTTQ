using System.Text.Json;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Homestay.DTO.Owners;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M4.2A: ghi chủ nhà và homestay qua các hàm ghi thật.</summary>
[Collection("M23 SQL")]
public sealed class M42AListingWriteTests
{
    private static byte[] File(int marker) => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, (byte)marker];
    private static CreateOwnerRequest OwnerValues() => new() { FullName = " Chủ M42A ", Phone = M14TestClient.Phone(), IdNumber = "I" + Guid.NewGuid().ToString("N")[..18],
        Email = "owner@example.test", BankName = " Ngân hàng M42A ", BankAccountNo = "123456789012345678901234", BankAccountName = "CHU M42A" };
    private static CreateHomestayRequest Values(HomestayItem h) => new() { OwnerId = h.OwnerId, AreaId = h.AreaId, Name = h.Name, Address = h.Address,
        ContactPhone = h.ContactPhone, Description = h.Description, CheckinGuide = h.CheckinGuide, CheckinTime = h.CheckinTime, CheckoutTime = h.CheckoutTime };
    private static CreateOwnerRequest Values(OwnerItem o) => new() { FullName = o.FullName, Phone = o.Phone, Email = o.Email, Address = o.Address };
    private static CreateCancellationPolicyRequest Policy(int percent = 0, bool active = true, CancellationScope scope = CancellationScope.All) =>
        new() { Name = "Chính sách M42A", Scope = scope, Activate = active, Rules = [new(7, 100), new(0, percent)] };
    /// <summary>Dữ liệu chuẩn bị sẵn cho các ca kiểm thử M4.2A.</summary>
    private sealed record Fixture(M41TestClient Manager, M41TestClient Staff, M21Data Source, OwnerItem Owner, HomestayDetail Detail);
    private static async Task<Fixture> DraftAsync(bool managerCreator = false)
    {
        var manager = new M41TestClient(); await manager.Base.LoginNewAsync(); var source = await M21Data.CreateAsync(manager.Base);
        var staff = new M41TestClient(); await staff.Base.LoginNewAsync(Position.Staff, source.AreaId, allAreas: false);
        var owner = await staff.Owners.CreateAsync(OwnerValues()); Assert.Equal(OwnerStatus.Pending, owner.Status);
        var raw = await manager.Owners.GetAsync(owner.Id);
        owner = await manager.Owners.ChangeStatusAsync(new(owner.Id, OwnerStatus.Approved, null, raw.RowVer));
        var creator = managerCreator ? manager : staff;
        var detail = await creator.Homes.CreateDraftAsync(new() { OwnerId = owner.Id, AreaId = source.AreaId, Name = "Nhà M42A " + Guid.NewGuid().ToString("N"),
            Address = " Địa chỉ M42A ", ContactPhone = "0901234567", Description = " Mô tả M42A " });
        return new(manager, staff, source, owner, detail);
    }
    // Round B room/contract writers stay stubbed. Only these dependencies are seeded directly in SQL.
    private static async Task<M21Data> SaleDependenciesAsync(Fixture f)
    {
        var home = f.Detail.Homestay.Id;
        await f.Manager.Base.MutateAsync("""
            DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
            INSERT dbo.Contracts(HomestayId,ContractNo,StartDate,EndDate,CommissionRate,Status,SignedByEmployeeId)
            VALUES(@home,@no,DATEADD(day,-1,@today),'2099-12-31',.12,'Active',@employee);
            INSERT dbo.RoomTypes(HomestayId,CategoryId,Name,MaxGuests,IsActive) VALUES(@home,@category,N'Loại M42A',4,1);
            DECLARE @type int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Rooms(RoomTypeId,RoomCode,IsActive) VALUES(@type,N'A1',1),(@type,N'A2',1);
            INSERT dbo.RoomTypePrices(RoomTypeId,DayType,Price,EffectiveFrom,CreatedByUserId)
            VALUES(@type,'Weekday',1000000,@today,@user),(@type,'Weekend',1200000,@today,@user),(@type,'Holiday',1500000,@today,@user);
            """, c => { c.Parameters.AddInt32("@home", home); c.Parameters.AddVarChar("@no", 30, "M42" + Guid.NewGuid().ToString("N")[..24]);
            c.Parameters.AddInt32("@category", f.Source.CategoryId); c.Parameters.AddInt32("@employee", f.Manager.Base.Current.RequireSession().EmployeeId); c.Parameters.AddInt32("@user", f.Manager.Base.Current.RequireSession().UserId); });
        var type = Assert.Single(await f.Manager.Rooms.ListTypesAsync(home));
        var rooms = await f.Manager.Rooms.ListRoomsAsync(home);
        return f.Source with { HomestayId = home, RoomTypeId = type.Id, Rooms = rooms.Select(r => r.Id).ToArray() };
    }
    private static async Task<HomestayDetail> DocumentsAsync(Fixture f)
    {
        var s = f.Staff; var h = f.Detail.Homestay;
        var d = await s.Homes.SetAmenitiesAsync(new(h.Id, [f.Source.AmenityId], h.RowVer));
        d = await s.Homes.AddImageAsync(new(h.Id, File(1), "C:\\fake\\cover.jpg", null, "Bìa", 1, true, d.Homestay.RowVer));
        d = await s.Homes.AddDocumentAsync(new(h.Id, DocumentType.BusinessLicense, File(2), "license.png", null, d.Homestay.RowVer));
        return await s.Homes.AddDocumentAsync(new(h.Id, DocumentType.FireSafety, File(3), "fire.png", null, d.Homestay.RowVer));
    }
    private static async Task<(Fixture F, M21Data Sale, HomestayDetail Active)> ActiveAsync()
    {
        var f = await DraftAsync(); var d = await DocumentsAsync(f); var sale = await SaleDependenciesAsync(f);
        d = await f.Staff.Homes.SetCancellationPolicyAsync(new(d.Homestay.Id, Policy(), d.Homestay.RowVer));
        d = await f.Staff.Homes.SubmitAsync(new(d.Homestay.Id, d.Homestay.RowVer));
        return (f, sale, await f.Manager.Homes.ApproveAsync(new(d.Homestay.Id, d.Homestay.RowVer)));
    }
    private static Task<int> Count(M41TestClient c, string sql, int id) => c.Base.ScalarAsync<int>(sql, p => p.Parameters.AddInt32("@id", id));
    private static AvailabilitySearchRequest Search(M21Data d) => new() { AreaId = d.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, Guests = 2 };
    private static CreateBookingRequest Booking(M21Data d) => new() { CustomerId = d.CustomerId, RoomTypeId = d.RoomTypeId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut,
        Guests = 2, ContactName = "Khách M42A", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone };
    [SqlFact]
    public async Task Flow910UsesRealWritesExactMissingChecklistAuditAndAvailability()
    {
        var f = await DraftAsync(); var d = await DocumentsAsync(f); var home = d.Homestay.Id;
        Assert.Equal("image/png", d.Images[0].File.ContentType); Assert.Equal("cover.jpg", d.Images[0].File.FileName);
        d = await f.Staff.Homes.SubmitAsync(new(home, d.Homestay.RowVer)); Assert.Equal(HomestayStatus.PendingReview, d.Homestay.Status); Assert.NotNull(d.Homestay.SubmittedAtUtc);
        var missing = (await f.Manager.Homes.GetApprovalConditionsAsync(home)).Conditions.Where(c => !c.IsSatisfied).ToArray();
        Assert.Equal(new[] { "ActiveContract", "SellableRoom" }, missing.Select(c => c.Code));
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.ApproveAsync(new(home, d.Homestay.RowVer)));
        Assert.Equal(string.Join(Environment.NewLine, missing.Select(c => c.Message)), error.Message);
        Assert.Equal(d.Homestay.RowVer, (await f.Manager.Homes.GetAsync(home)).Homestay.RowVer);
        var sale = await SaleDependenciesAsync(f);
        d = await f.Manager.Homes.ApproveAsync(new(home, d.Homestay.RowVer));
        Assert.Equal(HomestayStatus.Active, d.Homestay.Status); Assert.Equal(f.Manager.Base.Current.RequireSession().EmployeeId, d.Homestay.ApprovedByEmployeeId); Assert.NotNull(d.Homestay.ApprovedAtUtc);
        Assert.Contains(await f.Staff.Base.Availability.SearchAsync(Search(sale)), r => r.HomestayId == home);
        Assert.Equal(new[] { "Homestay.Create", "Homestay.SetAmenities", "Homestay.AddImage", "Homestay.AddDocument", "Homestay.AddDocument", "Homestay.Submit", "Homestay.Approve" }, d.Audit.Select(a => a.ActionCode));
        Assert.Equal(2, d.Audit.Count(a => a.NewValues!.Contains("\"Status\":\"PendingReview\"", StringComparison.Ordinal) || a.NewValues!.Contains("\"Status\":\"Active\"", StringComparison.Ordinal)));
        Assert.Equal(2, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Owners' AND RecordId=CONVERT(varchar(50),@id);", f.Owner.Id));
        Assert.All(d.Audit, a => Assert.NotEqual(default, a.CreatedAtUtc));
    }
    [SqlFact]
    public async Task MissingAllNineConditionsReturnsExactlyTheReadChecklistMessages()
    {
        var f = await DraftAsync(); var d = f.Detail;
        // Create requires address, but legacy drafts may have blanks. Approval must report every missing item.
        await f.Manager.Base.MutateAsync("UPDATE dbo.Owners SET Status='Suspended' WHERE Id=@owner;UPDATE dbo.Homestays SET Address=N' ',ContactPhone=NULL,Description=NULL WHERE Id=@id;", c => { c.Parameters.AddInt32("@owner", f.Owner.Id); c.Parameters.AddInt32("@id", d.Homestay.Id); });
        d = await f.Staff.Homes.GetAsync(d.Homestay.Id); d = await f.Staff.Homes.SubmitAsync(new(d.Homestay.Id, d.Homestay.RowVer));
        var checklist = await f.Manager.Homes.GetApprovalConditionsAsync(d.Homestay.Id); Assert.Equal(9, checklist.Conditions.Count(c => !c.IsSatisfied));
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.ApproveAsync(new(d.Homestay.Id, d.Homestay.RowVer)));
        Assert.Equal(string.Join(Environment.NewLine, checklist.Conditions.Select(c => c.Message)), error.Message);
        Assert.Equal(2, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id);", d.Homestay.Id));
    }
    [SqlFact]
    public async Task D11StaffCanEditAnotherCreatorsDraftButOtherStatesAndAreasAreBlocked()
    {
        var f = await DraftAsync(); var home = f.Detail.Homestay.Id;
        var other = new M41TestClient(); await other.Base.LoginNewAsync(Position.Staff, f.Source.AreaId, allAreas: false);
        var d = await other.Homes.UpdateAsync(new(home, Values(f.Detail.Homestay) with { Description = "NV khác sửa" }, f.Detail.Homestay.RowVer));
        Assert.Equal(f.Detail.Homestay.CreatedByUserId, d.Homestay.CreatedByUserId);
        var outside = new M41TestClient(); await outside.Base.LoginNewAsync(Position.Staff, allAreas: false);
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Homes.UpdateAsync(new(home, Values(d.Homestay), d.Homestay.RowVer)));
        d = await other.Homes.SubmitAsync(new(home, d.Homestay.RowVer));
        await Assert.ThrowsAsync<ForbiddenException>(() => other.Homes.UpdateAsync(new(home, Values(d.Homestay), d.Homestay.RowVer)));
        await f.Manager.Base.MutateAsync("UPDATE dbo.Homestays SET Status='Active' WHERE Id=@id;", c => c.Parameters.AddInt32("@id", home));
        d = await f.Manager.Homes.GetAsync(home);
        await Assert.ThrowsAsync<ForbiddenException>(() => other.Homes.SetAmenitiesAsync(new(home, [], d.Homestay.RowVer)));
    }
    [SqlFact]
    public async Task D11OriginalManagerCreatorCannotApproveAfterOtherStaffEditsAndSubmits()
    {
        var f = await DraftAsync(managerCreator: true); var d = await f.Staff.Homes.UpdateAsync(new(f.Detail.Homestay.Id, Values(f.Detail.Homestay), f.Detail.Homestay.RowVer));
        d = await f.Staff.Homes.SubmitAsync(new(d.Homestay.Id, d.Homestay.RowVer));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Manager.Homes.ApproveAsync(new(d.Homestay.Id, d.Homestay.RowVer)));
        Assert.Equal(f.Manager.Base.Current.RequireSession().UserId, d.Homestay.CreatedByUserId);
    }
    [SqlFact]
    public async Task D11SuspendingOwnerKeepsActiveListingButBlocksReopenWithOwnerApprovedMessage()
    {
        var (f, _, active) = await ActiveAsync();
        var owner = await f.Manager.Owners.ChangeStatusAsync(new(f.Owner.Id, OwnerStatus.Suspended, " Ngừng chủ ", f.Owner.RowVer));
        Assert.Equal(OwnerStatus.Suspended, owner.Status);
        var d = await f.Manager.Homes.GetAsync(active.Homestay.Id); Assert.Equal(HomestayStatus.Active, d.Homestay.Status); Assert.Equal(active.Homestay.RowVer, d.Homestay.RowVer);
        d = await f.Manager.Homes.SuspendAsync(new(d.Homestay.Id, d.Homestay.RowVer, " Tạm ngưng "));
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.ReopenAsync(new(d.Homestay.Id, d.Homestay.RowVer, "Mở lại")));
        Assert.Equal("Chủ chưa được duyệt.", error.Message);
        owner = await f.Manager.Owners.ChangeStatusAsync(new(owner.Id, OwnerStatus.Approved, "Đủ điều kiện trở lại", owner.RowVer));
        d = await f.Manager.Homes.ReopenAsync(new(d.Homestay.Id, d.Homestay.RowVer, " Mở lại ")); Assert.Equal(HomestayStatus.Active, d.Homestay.Status); Assert.Equal("Mở lại", d.Homestay.StatusReason);
    }
    [SqlFact]
    public async Task D11AddressOrAreaChangeHoldsExistingBookingsAndBlocksSearchAndNewBooking()
    {
        foreach (var areaChange in new[] { false, true })
        {
            var (f, sale, active) = await ActiveAsync(); var home = active.Homestay.Id;
            var booking = await f.Staff.Base.Bookings.CreateAsync(Booking(sale));
            var before = await f.Manager.Base.ScalarAsync<string>("SELECT CONVERT(nvarchar(max),(SELECT b.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)) FROM dbo.Bookings b WHERE Id=@id;", c => c.Parameters.AddInt32("@id", booking.Booking.Id));
            var newArea = areaChange ? await f.Manager.Base.ScalarAsync<int>("SELECT TOP(1) Id FROM dbo.Areas WHERE Id<>@area ORDER BY Id;", c => c.Parameters.AddInt32("@area", active.Homestay.AreaId)) : active.Homestay.AreaId;
            var d = await f.Manager.Homes.UpdateAsync(new(home, Values(active.Homestay) with { Address = areaChange ? active.Homestay.Address : "Địa chỉ mới", AreaId = newArea }, active.Homestay.RowVer));
            Assert.Equal(HomestayStatus.PendingReview, d.Homestay.Status);
            Assert.Equal(before, await f.Manager.Base.ScalarAsync<string>("SELECT CONVERT(nvarchar(max),(SELECT b.* FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)) FROM dbo.Bookings b WHERE Id=@id;", c => c.Parameters.AddInt32("@id", booking.Booking.Id)));
            Assert.Equal(2, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", booking.Booking.Id));
            Assert.DoesNotContain(await f.Staff.Base.Availability.SearchAsync(Search(sale) with { AreaId = null }), x => x.HomestayId == home);
            await Assert.ThrowsAsync<BusinessException>(() => f.Staff.Base.Bookings.CreateAsync(Booking(sale) with { CheckIn = M21Data.CheckIn.AddDays(5), CheckOut = M21Data.CheckOut.AddDays(5) }));
            Assert.Contains("PendingReview", d.Audit.Last().NewValues!); Assert.Contains("Active", d.Audit.Last().OldValues!);
        }
    }
    [SqlFact]
    public async Task D11NormalizationAndOrdinaryEditsKeepActiveAndScopeChecksBothAreasAndNewOwner()
    {
        var (f, sale, active) = await ActiveAsync();
        var d = await f.Manager.Homes.UpdateAsync(new(active.Homestay.Id, Values(active.Homestay) with { Address = "  " + active.Homestay.Address + "  ", Name = "Tên mới", Description = "Mô tả mới" }, active.Homestay.RowVer));
        Assert.Equal(HomestayStatus.Active, d.Homestay.Status); Assert.NotEqual(active.Homestay.RowVer, d.Homestay.RowVer);
        var scoped = new M41TestClient(); await scoped.Base.LoginNewAsync(Position.Manager, sale.AreaId, allAreas: false);
        var outside = await f.Manager.Base.ScalarAsync<int>("SELECT TOP(1) Id FROM dbo.Areas WHERE Id<>@id;", c => c.Parameters.AddInt32("@id", sale.AreaId));
        await Assert.ThrowsAsync<NotFoundException>(() => scoped.Homes.UpdateAsync(new(d.Homestay.Id, Values(d.Homestay) with { AreaId = outside }, d.Homestay.RowVer)));
        var noAreas = new M41TestClient(); await noAreas.Base.LoginNewAsync(Position.Manager, allAreas: false);
        await Assert.ThrowsAsync<NotFoundException>(() => noAreas.Homes.UpdateAsync(new(d.Homestay.Id, Values(d.Homestay) with { AreaId = outside }, d.Homestay.RowVer)));
        var stranger = await f.Manager.Owners.CreateAsync(OwnerValues());
        await Assert.ThrowsAsync<NotFoundException>(() => scoped.Homes.UpdateAsync(new(d.Homestay.Id, Values(d.Homestay) with { OwnerId = stranger.Id }, d.Homestay.RowVer)));
        Assert.Equal(d.Homestay.RowVer, (await f.Manager.Homes.GetAsync(d.Homestay.Id)).Homestay.RowVer);
    }
    [SqlFact]
    public async Task D11LegalRemovalAndReloadSupersedeWithoutDeletingFilesOrExistingOrders()
    {
        foreach (var reload in new[] { false, true })
        foreach (var documentType in new[] { DocumentType.BusinessLicense, DocumentType.FireSafety })
        {
            var (f, sale, d) = await ActiveAsync(); var home = d.Homestay.Id; var booking = await sale.BookingAsync(f.Manager.Base, 0, BookingStatus.Confirmed);
            var old = d.Documents.Single(x => x.DocumentType == documentType && !x.IsSuperseded);
            d = reload ? await f.Manager.Homes.AddDocumentAsync(new(home, documentType, File(44), "new.pdf", null, d.Homestay.RowVer))
                : await f.Manager.Homes.RemoveDocumentAsync(new(home, old.Id, " Thu hồi giấy phép ", d.Homestay.RowVer));
            Assert.Equal(HomestayStatus.PendingReview, d.Homestay.Status); Assert.True(d.Documents.Single(x => x.Id == old.Id).IsSuperseded);
            Assert.Equal(reload ? 3 : 2, d.Documents.Count); Assert.Equal(reload ? 1 : 0, d.Documents.Count(x => x.DocumentType == documentType && !x.IsSuperseded));
            Assert.Equal(File(documentType == DocumentType.BusinessLicense ? 2 : 3), (await f.Manager.Files.GetHomestayDocumentAsync(home, old.File.FileId)).Content);
            Assert.Equal(2, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", booking));
            Assert.DoesNotContain(await f.Manager.Base.Availability.SearchAsync(Search(sale)), x => x.HomestayId == home);
            Assert.Equal(reload ? 3 : 1, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode=@action;", home, reload ? "Homestay.AddDocument" : "Homestay.RemoveDocument"));
        }
    }
    private static Task<int> Count(M41TestClient c, string sql, int id, string action) => c.Base.ScalarAsync<int>(sql, p => { p.Parameters.AddInt32("@id", id); p.Parameters.AddVarChar("@action", 50, action); });
    [SqlFact]
    public async Task ImagesHaveOneCoverParentVersionAndActiveCannotLoseCover()
    {
        var (f, sale, d) = await ActiveAsync(); var home = d.Homestay.Id; var first = Assert.Single(d.Images);
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.RemoveImageAsync(new(home, first.Id, d.Homestay.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.UpdateImageAsync(new(home, first.Id, null, null, 0, false, d.Homestay.RowVer)));
        var version = d.Homestay.RowVer;
        d = await f.Manager.Homes.AddImageAsync(new(home, File(10), "second.webp", sale.RoomTypeId, " Second ", 0, true, version));
        Assert.NotEqual(version, d.Homestay.RowVer); var second = Assert.Single(d.Images, i => i.IsCover); Assert.NotEqual(first.Id, second.Id);
        d = await f.Manager.Homes.UpdateImageAsync(new(home, first.Id, null, "Trở lại bìa", 2, true, d.Homestay.RowVer));
        Assert.Equal(first.Id, Assert.Single(d.Images, i => i.IsCover).Id);
        d = await f.Manager.Homes.RemoveImageAsync(new(home, second.Id, d.Homestay.RowVer)); Assert.Single(d.Images);
        Assert.Equal(File(10), (await f.Manager.Base.Factory.ReadAsync(async (db, ct) =>
        { using var c = db.CreateCommand("SELECT Content FROM dbo.StoredFiles WHERE Id=@file;"); c.Parameters.AddGuid("@file", second.File.FileId); return (byte[])(await db.ScalarAsync(c, ct))!; }, CancellationToken.None)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.AddImageAsync(new(home, File(1), "duplicate.png", null, null, 0, false, d.Homestay.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Manager.Homes.AddImageAsync(new(home, File(11), "cross.png", f.Source.RoomTypeId, null, 0, false, d.Homestay.RowVer)));
        var other = await f.Manager.Homes.GetAsync(f.Source.HomestayId);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Manager.Homes.RemoveImageAsync(new(f.Source.HomestayId, first.Id, other.Homestay.RowVer)));
    }
    [SqlFact]
    public async Task InvalidAndOversizedFilesNeverWriteRowsOrBumpParent()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay; var s = f.Staff;
        var count = await f.Manager.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;");
        foreach (var bytes in new[] { new byte[] { 0x4d, 0x5a, 0x90 }, new byte[ReceiptRules.MaxEvidenceBytes + 1], Array.Empty<byte>() })
        {
            await Assert.ThrowsAsync<BusinessException>(() => s.Homes.AddImageAsync(new(h.Id, bytes, "fake.jpg", null, null, 0, true, h.RowVer)));
            await Assert.ThrowsAsync<BusinessException>(() => s.Homes.AddDocumentAsync(new(h.Id, DocumentType.FireSafety, bytes, "fake.pdf", null, h.RowVer)));
        }
        Assert.Equal(count, await f.Manager.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;"));
        Assert.Equal(h.RowVer, (await s.Homes.GetAsync(h.Id)).Homestay.RowVer);
        Assert.Equal(1, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id);", h.Id));
    }
    [SqlFact]
    public async Task AmenitiesReplaceSetAndRejectMissingOrInactiveCatalogEntriesAtomically()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay;
        var d = await f.Staff.Homes.SetAmenitiesAsync(new(h.Id, [f.Source.AmenityId, f.Source.AmenityId], h.RowVer)); Assert.Single(d.Amenities);
        foreach (var id in new[] { int.MaxValue, f.Source.AmenityId })
        {
            if (id == f.Source.AmenityId) await f.Manager.Base.MutateAsync("UPDATE dbo.Amenities SET IsActive=0 WHERE Id=@id;", c => c.Parameters.AddInt32("@id", id));
            await Assert.ThrowsAsync<BusinessException>(() => f.Staff.Homes.SetAmenitiesAsync(new(h.Id, [id], d.Homestay.RowVer)));
            Assert.Equal(d.Homestay.RowVer, (await f.Staff.Homes.GetAsync(h.Id)).Homestay.RowVer); Assert.Single((await f.Staff.Homes.GetAsync(h.Id)).Amenities);
        }
        d = await f.Staff.Homes.SetAmenitiesAsync(new(h.Id, [], d.Homestay.RowVer)); Assert.Empty(d.Amenities);
    }
    [SqlFact]
    public async Task PoliciesUseSharedRulesVersionContentAndPreserveBookedPolicySnapshot()
    {
        var (f, sale, d) = await ActiveAsync(); var home = d.Homestay.Id;
        var booked = await f.Staff.Base.Bookings.CreateAsync(Booking(sale));
        var oldPolicy = Assert.Single(d.CancellationPolicies, p => p.IsActive);
        var rulesBefore = JsonSerializer.Serialize(oldPolicy.Rules);
        var invalid = Policy() with { Rules = [new(1, 100)] };
        var expected = Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate(invalid.Rules));
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.SetCancellationPolicyAsync(new(home, invalid, d.Homestay.RowVer))); Assert.Equal(expected.Message, error.Message);
        d = await f.Manager.Homes.SetCancellationPolicyAsync(new(home, Policy(25), d.Homestay.RowVer));
        Assert.False(d.CancellationPolicies.Single(p => p.Id == oldPolicy.Id).IsActive);
        Assert.Equal(rulesBefore, JsonSerializer.Serialize(d.CancellationPolicies.Single(p => p.Id == oldPolicy.Id).Rules));
        Assert.Equal(oldPolicy.Id, await Count(f.Manager, "SELECT CancellationPolicyId FROM dbo.Bookings WHERE Id=@id;", booked.Booking.Id));
        var newPolicy = Assert.Single(d.CancellationPolicies, p => p.IsActive); Assert.NotEqual(oldPolicy.Id, newPolicy.Id);
        var nextBooking = await f.Staff.Base.Bookings.CreateAsync(Booking(sale) with { CheckIn = M21Data.CheckIn.AddDays(10), CheckOut = M21Data.CheckOut.AddDays(10) });
        Assert.Equal(newPolicy.Id, await Count(f.Manager, "SELECT CancellationPolicyId FROM dbo.Bookings WHERE Id=@id;", nextBooking.Booking.Id));
        d = await f.Manager.Homes.SetCancellationPolicyAsync(new(home, Policy(10, active: false), d.Homestay.RowVer)); Assert.Equal(newPolicy.Id, Assert.Single(d.CancellationPolicies, p => p.IsActive).Id);
        d = await f.Manager.Homes.DisableCancellationPolicyAsync(new(home, newPolicy.Id, d.Homestay.RowVer)); Assert.DoesNotContain(d.CancellationPolicies, p => p.IsActive);
        var quote = await f.Manager.Base.Availability.QuoteAsync(new(sale.RoomTypeId, M21Data.CheckIn.AddDays(20), M21Data.CheckOut.AddDays(20), 2)); Assert.Null(quote.RoomType.CancellationPolicy.HomestayId);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Manager.Homes.DisableCancellationPolicyAsync(new(home, quote.RoomType.CancellationPolicy.Id, d.Homestay.RowVer)));
        Assert.Equal(oldPolicy.Id, await Count(f.Manager, "SELECT CancellationPolicyId FROM dbo.Bookings WHERE Id=@id;", booked.Booking.Id));
    }
    [SqlFact]
    public async Task EndBlocksActiveContractThenOccupiedBookingAndAllowsCompletedLedger()
    {
        var (f, sale, d) = await ActiveAsync(); var home = d.Homestay.Id;
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.EndAsync(new(home, d.Homestay.RowVer, "Kết thúc"))); Assert.Contains("hợp đồng Active", error.Message);
        var booking = await sale.BookingAsync(f.Manager.Base, 0, BookingStatus.Confirmed);
        await f.Manager.Base.MutateAsync("UPDATE dbo.Contracts SET Status='Expired' WHERE HomestayId=@id;", c => c.Parameters.AddInt32("@id", home));
        error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.EndAsync(new(home, d.Homestay.RowVer, "Kết thúc"))); Assert.Contains("đơn đang chiếm phòng", error.Message);
        await f.Manager.Base.MutateAsync("UPDATE dbo.Bookings SET Status='Completed',CheckedInAt=SYSUTCDATETIME(),CompletedAt=SYSUTCDATETIME() WHERE Id=@id;", c => c.Parameters.AddInt32("@id", booking));
        d = await f.Manager.Homes.EndAsync(new(home, d.Homestay.RowVer, " Kết thúc ")); Assert.Equal(HomestayStatus.Ended, d.Homestay.Status); Assert.Equal("Kết thúc", d.Homestay.StatusReason);
        Assert.Equal(2, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", booking));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.SubmitAsync(new(home, d.Homestay.RowVer)));
    }
    [SqlFact]
    public async Task ReturnSubmitAndReopenReasonsRespectStateMachineAndBoundaries()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay;
        var d = await f.Staff.Homes.SubmitAsync(new(h.Id, h.RowVer));
        foreach (var reason in new[] { null, " ", new string('x', 501) })
            await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.ReturnAsync(new(h.Id, d.Homestay.RowVer, reason)));
        d = await f.Manager.Homes.ReturnAsync(new(h.Id, d.Homestay.RowVer, " Bổ sung hồ sơ ")); Assert.Equal(HomestayStatus.Draft, d.Homestay.Status); Assert.Equal("Bổ sung hồ sơ", d.Homestay.StatusReason);
        d = await f.Staff.Homes.SubmitAsync(new(h.Id, d.Homestay.RowVer)); Assert.Null(d.Homestay.StatusReason);
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.SuspendAsync(new(h.Id, d.Homestay.RowVer, "Sai trạng thái")));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.ReopenAsync(new(h.Id, d.Homestay.RowVer, "Sai trạng thái")));
        Assert.Equal(h.CreatedByUserId, d.Homestay.CreatedByUserId);
    }
    [SqlFact]
    public async Task EveryExistingListingWriterRejectsStaleParentToken()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay; var m = f.Manager;
        var current = await m.Homes.SetAmenitiesAsync(new(h.Id, [], h.RowVer)); Assert.NotEqual(h.RowVer, current.Homestay.RowVer);
        Func<Task>[] writes = [
            () => m.Homes.UpdateAsync(new(h.Id, Values(h), h.RowVer)), () => m.Homes.SetAmenitiesAsync(new(h.Id, [], h.RowVer)),
            () => m.Homes.AddImageAsync(new(h.Id, File(33), "x.png", null, null, 0, true, h.RowVer)),
            () => m.Homes.UpdateImageAsync(new(h.Id, 1, null, null, 0, true, h.RowVer)), () => m.Homes.RemoveImageAsync(new(h.Id, 1, h.RowVer)),
            () => m.Homes.AddDocumentAsync(new(h.Id, DocumentType.BusinessLicense, File(33), "x.png", null, h.RowVer)),
            () => m.Homes.RemoveDocumentAsync(new(h.Id, 1, "Lý do", h.RowVer)), () => m.Homes.SubmitAsync(new(h.Id, h.RowVer)),
            () => m.Homes.ApproveAsync(new(h.Id, h.RowVer)), () => m.Homes.ReturnAsync(new(h.Id, h.RowVer, "Lý do")),
            () => m.Homes.SuspendAsync(new(h.Id, h.RowVer, "Lý do")), () => m.Homes.ReopenAsync(new(h.Id, h.RowVer, "Lý do")),
            () => m.Homes.EndAsync(new(h.Id, h.RowVer, "Lý do")), () => m.Homes.SetCancellationPolicyAsync(new(h.Id, Policy(), h.RowVer)),
            () => m.Homes.DisableCancellationPolicyAsync(new(h.Id, 1, h.RowVer)) ];
        Assert.Equal(15, writes.Length); foreach (var write in writes) await Assert.ThrowsAsync<ConcurrencyException>(write);
        Assert.Equal(current.Homestay.RowVer, (await m.Homes.GetAsync(h.Id)).Homestay.RowVer);
    }
    private static async Task<Exception?> Attempt(Task start, Func<Task> operation)
    {
        await start;
        try { await operation(); return null; } catch (Exception e) { return e; }
    }
    [SqlFact]
    public async Task TwoManagersApprovingConcurrentlyHaveExactlyOneWinner()
    {
        var f = await DraftAsync(); var d = await DocumentsAsync(f); _ = await SaleDependenciesAsync(f);
        d = await f.Staff.Homes.SubmitAsync(new(d.Homestay.Id, d.Homestay.RowVer));
        var other = new M41TestClient(); await other.Base.LoginNewAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Attempt(start.Task, () => f.Manager.Homes.ApproveAsync(new(d.Homestay.Id, d.Homestay.RowVer)));
        var second = Attempt(start.Task, () => other.Homes.ApproveAsync(new(d.Homestay.Id, d.Homestay.RowVer)));
        start.SetResult(); var result = await Task.WhenAll(first, second);
        Assert.Single(result, e => e is null); Assert.IsType<ConcurrencyException>(Assert.Single(result, e => e is not null));
        Assert.Equal(1, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Homestays' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Homestay.Approve';", d.Homestay.Id));
    }
    [SqlFact]
    public async Task TwoManagersSettingCoverConcurrentlyKeepExactlyOneCoverAndStaleLoser()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay;
        var d = await f.Staff.Homes.AddImageAsync(new(h.Id, File(1), "one.png", null, null, 0, true, h.RowVer));
        d = await f.Staff.Homes.AddImageAsync(new(h.Id, File(2), "two.png", null, null, 1, false, d.Homestay.RowVer));
        var other = new M41TestClient(); await other.Base.LoginNewAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Attempt(start.Task, () => f.Manager.Homes.UpdateImageAsync(new(h.Id, d.Images[0].Id, null, null, 0, true, d.Homestay.RowVer)));
        var second = Attempt(start.Task, () => other.Homes.UpdateImageAsync(new(h.Id, d.Images[1].Id, null, null, 0, true, d.Homestay.RowVer)));
        start.SetResult(); var result = await Task.WhenAll(first, second);
        Assert.Single(result, e => e is null); Assert.IsType<ConcurrencyException>(Assert.Single(result, e => e is not null));
        Assert.Single((await f.Manager.Homes.GetAsync(h.Id)).Images, i => i.IsCover);
        Assert.Equal(1, await Count(f.Manager, "SELECT COUNT(*) FROM dbo.HomestayImages WHERE HomestayId=@id AND IsCover=1;", h.Id));
    }
    [SqlFact]
    public async Task OwnerUniquePhoneIdentityNormalizationAndSnapshotConcurrencyAreEnforced()
    {
        var m = new M41TestClient(); await m.Base.LoginNewAsync(); var input = OwnerValues(); var o = await m.Owners.CreateAsync(input);
        Assert.Equal(32, o.RowVer.Length); Assert.Equal("Chủ M42A", o.FullName); Assert.Equal(PhoneNumber.Normalize(input.Phone), o.Phone);
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() => m.Owners.CreateAsync(OwnerValues() with { Phone = o.Phone }));
        Assert.Equal("UQ_Owners_Phone", duplicate.ConstraintName); Assert.Equal(ConstraintMessages.GetMessage("UQ_Owners_Phone"), duplicate.Message);
        duplicate = await Assert.ThrowsAsync<BusinessException>(() => m.Owners.CreateAsync(OwnerValues() with { IdNumber = o.IdNumber })); Assert.Equal("UX_Owners_IdNumber", duplicate.ConstraintName);
        var updated = await m.Owners.UpdateAsync(new(o.Id, Values(o) with { FullName = "Chủ sửa tên" }, o.RowVer));
        Assert.Equal(o.IdNumber, updated.IdNumber); Assert.Equal(o.BankAccountNo, updated.BankAccountNo); Assert.NotEqual(o.RowVer, updated.RowVer);
        await Assert.ThrowsAsync<ConcurrencyException>(() => m.Owners.UpdateAsync(new(o.Id, Values(o), o.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => m.Owners.ChangeBankAsync(new(o.Id, "VCB", "1234", "NAME", "Xác minh", o.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Approved, null, o.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => m.Owners.UpdateAsync(new(o.Id, Values(o), new byte[8])));
        updated = await m.Owners.UpdateAsync(new(o.Id, Values(updated), updated.RowVer, ClearIdNumber: true)); Assert.Null(updated.IdNumber);
        var nextId = "I" + Guid.NewGuid().ToString("N")[..18];
        updated = await m.Owners.UpdateAsync(new(o.Id, Values(updated) with { IdNumber = nextId }, updated.RowVer)); Assert.Equal(nextId, updated.IdNumber);
        await Assert.ThrowsAsync<BusinessException>(() => m.Owners.UpdateAsync(new(o.Id, Values(updated) with { IdNumber = "new" }, updated.RowVer, ClearIdNumber: true)));
    }
    [SqlFact]
    public async Task OwnerBankCanOnlyChangeThroughVerifiedManagerActionAndAuditAlwaysMasksSecrets()
    {
        var f = await DraftAsync(); var o = await f.Manager.Owners.GetAsync(f.Owner.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Owners.UpdateAsync(new(o.Id, Values(o) with { BankAccountNo = "9999999999" }, o.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Owners.UpdateAsync(new(o.Id, Values(o) with { BankAccountNo = "9999999999" }, o.RowVer)));
        // Pending staff may update ordinary fields but cannot alter bank; use a fresh owner to reach that branch.
        var input = OwnerValues(); var pending = await f.Staff.Owners.CreateAsync(input);
        await Assert.ThrowsAsync<BusinessException>(() => f.Staff.Owners.UpdateAsync(new(pending.Id, Values(pending) with { BankName = "Khác" }, pending.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Owners.ChangeBankAsync(new(pending.Id, "VCB", "999", "X", "Đã xác minh", pending.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Owners.ChangeBankAsync(new(o.Id, "VCB", "999", "X", " ", o.RowVer)));
        var changed = await f.Manager.Owners.ChangeBankAsync(new(o.Id, " VCB ", " 998877665544332211 ", " NEW OWNER ", " Đã xác minh qua điện thoại ", o.RowVer));
        Assert.Equal("998877665544332211", changed.BankAccountNo); Assert.Equal("VCB", changed.BankName);
        changed = await f.Manager.Owners.UpdateAsync(new(o.Id, Values(changed), changed.RowVer)); Assert.Equal("998877665544332211", changed.BankAccountNo);
        var logs = await f.Manager.Base.Factory.ReadAsync(async (db, ct) =>
        {
            using var c = db.CreateCommand("SELECT OldValues,NewValues,Note FROM dbo.AuditLogs WHERE TableName='Owners' AND RecordId=@id ORDER BY Id;");
            c.Parameters.AddVarChar("@id", 50, o.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return await db.QueryAsync(c, r => (r.GetNullableString("OldValues") ?? "") + (r.GetNullableString("NewValues") ?? "") + (r.GetNullableString("Note") ?? ""), ct);
        }, CancellationToken.None);
        Assert.Equal(4, logs.Count);
        foreach (var log in logs)
        {
            Assert.DoesNotContain(o.IdNumber!, log, StringComparison.Ordinal); Assert.DoesNotContain(o.BankAccountNo!, log, StringComparison.Ordinal);
            Assert.DoesNotContain("998877665544332211", log, StringComparison.Ordinal); Assert.DoesNotContain("NEW OWNER", log, StringComparison.Ordinal);
        }
    }
    [SqlFact]
    public async Task OwnerStatusSupportsOnlyContractTransitionsAndApprovedRequiresBank()
    {
        var m = new M41TestClient(); await m.Base.LoginNewAsync(); var o = await m.Owners.CreateAsync(OwnerValues() with { BankName = null, BankAccountNo = null, BankAccountName = null });
        var error = await Assert.ThrowsAsync<BusinessException>(() => m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Approved, null, o.RowVer))); Assert.Contains("bổ sung đủ ngân hàng", error.Message);
        await Assert.ThrowsAsync<BusinessException>(() => m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Rejected, " ", o.RowVer)));
        o = await m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Rejected, " Thiếu hồ sơ ", o.RowVer)); Assert.Equal(OwnerStatus.Rejected, o.Status);
        await Assert.ThrowsAsync<BusinessException>(() => m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Approved, "Sai cặp", o.RowVer)));
        o = await m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Pending, "Bổ sung", o.RowVer));
        o = await m.Owners.ChangeBankAsync(new(o.Id, "VCB", "111222333444", "OWNER", "Xác minh", o.RowVer));
        o = await m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Approved, null, o.RowVer)); Assert.Equal(m.Base.Current.RequireSession().EmployeeId, o.ApprovedByEmployeeId); Assert.NotNull(o.ApprovedAtUtc);
        o = await m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Suspended, new string('x', 500), o.RowVer));
        o = await m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Approved, "Mở lại", o.RowVer)); Assert.Equal(OwnerStatus.Approved, o.Status);
        await Assert.ThrowsAsync<BusinessException>(() => m.Owners.ChangeStatusAsync(new(o.Id, OwnerStatus.Rejected, "Sai cặp", o.RowVer)));
    }
    [SqlFact]
    public async Task OwnerPendingEditsRequireScopeAndStaffCannotEditApproved()
    {
        var f = await DraftAsync(); var input = OwnerValues(); var pending = await f.Staff.Owners.CreateAsync(input);
        var other = new M41TestClient(); await other.Base.LoginNewAsync(Position.Staff, f.Source.AreaId, allAreas: false);
        await Assert.ThrowsAsync<NotFoundException>(() => other.Owners.UpdateAsync(new(pending.Id, Values(pending), pending.RowVer)));
        pending = await f.Staff.Owners.UpdateAsync(new(pending.Id, Values(pending) with { FullName = "Được sửa" }, pending.RowVer));
        Assert.Equal("Được sửa", pending.FullName); Assert.Equal(f.Staff.Base.Current.RequireSession().EmployeeId, pending.CreatedByEmployeeId);
        var owner = await f.Manager.Owners.GetAsync(f.Owner.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => f.Staff.Owners.UpdateAsync(new(owner.Id, Values(owner), owner.RowVer)));
    }
    [SqlFact]
    public async Task ChildIdsCannotCrossParentsAndOtherDocumentTypesDoNotSupersedeEachOther()
    {
        var f = await DraftAsync(); var d = f.Detail; var home = d.Homestay.Id;
        d = await f.Staff.Homes.AddDocumentAsync(new(home, DocumentType.Other, File(20), "one.png", null, d.Homestay.RowVer));
        d = await f.Staff.Homes.AddDocumentAsync(new(home, DocumentType.Other, File(21), "two.png", null, d.Homestay.RowVer));
        Assert.Equal(2, d.Documents.Count); Assert.DoesNotContain(d.Documents, x => x.IsSuperseded);
        var other = await f.Manager.Homes.GetAsync(f.Source.HomestayId);
        await Assert.ThrowsAsync<NotFoundException>(() => f.Manager.Homes.RemoveDocumentAsync(new(other.Homestay.Id, d.Documents[0].Id, "Sai parent", other.Homestay.RowVer)));
        d = await f.Staff.Homes.RemoveDocumentAsync(new(home, d.Documents[0].Id, " Lưu lịch sử ", d.Homestay.RowVer));
        Assert.Equal(1, d.Documents.Count(x => x.IsSuperseded)); Assert.Equal(HomestayStatus.Draft, d.Homestay.Status);
        var policy = await f.Staff.Homes.SetCancellationPolicyAsync(new(home, Policy(), d.Homestay.RowVer));
        await Assert.ThrowsAsync<NotFoundException>(() => f.Manager.Homes.DisableCancellationPolicyAsync(new(other.Homestay.Id, policy.CancellationPolicies[0].Id, other.Homestay.RowVer)));
    }
    [SqlFact]
    public async Task UniqueFailureRollsBackStatusFieldsParentVersionAndAuditTogether()
    {
        var f = await DraftAsync(); var first = f.Detail.Homestay;
        var second = await f.Staff.Homes.CreateDraftAsync(Values(first) with { Name = "Nhà thứ hai" }); var before = second.Homestay;
        var error = await Assert.ThrowsAsync<BusinessException>(() => f.Manager.Homes.UpdateAsync(new(before.Id, Values(before) with { Name = first.Name, Address = "Địa chỉ mới" }, before.RowVer)));
        Assert.Equal("UQ_Homestays_OwnerId_Name", error.ConstraintName);
        var after = await f.Manager.Homes.GetAsync(before.Id); Assert.Equal(before.RowVer, after.Homestay.RowVer); Assert.Equal(before.Address, after.Homestay.Address); Assert.Single(after.Audit);
    }
    [SqlFact]
    public async Task RealSessionValidatorRejectsRevokedSessionForBothServicesWithoutWrites()
    {
        var f = await DraftAsync(); var h = f.Detail.Homestay;
        var raw = await f.Manager.Owners.GetAsync(f.Owner.Id);
        await f.Manager.Base.MutateAsync("UPDATE dbo.Users SET SecurityStamp=NEWID() WHERE Id=@id;", c => c.Parameters.AddInt32("@id", f.Manager.Base.Current.RequireSession().UserId));
        await Assert.ThrowsAsync<SessionExpiredException>(() => f.Manager.Homes.SetAmenitiesAsync(new(h.Id, [], h.RowVer)));
        await Assert.ThrowsAsync<SessionExpiredException>(() => f.Manager.Owners.UpdateAsync(new(raw.Id, Values(raw), raw.RowVer)));
        Assert.Equal(h.RowVer, (await f.Staff.Homes.GetAsync(h.Id)).Homestay.RowVer);
    }
    [SqlFact]
    public async Task AccountantIsForbiddenBeforeNullValidationForAllTwentyWrites()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(Position.Accountant);
        Func<Task>[] writes = [ () => c.Owners.CreateAsync(null!), () => c.Owners.UpdateAsync(null!), () => c.Owners.ChangeBankAsync(null!), () => c.Owners.ChangeStatusAsync(null!),
            () => c.Homes.CreateDraftAsync(null!), () => c.Homes.UpdateAsync(null!), () => c.Homes.SetAmenitiesAsync(null!), () => c.Homes.AddImageAsync(null!),
            () => c.Homes.UpdateImageAsync(null!), () => c.Homes.RemoveImageAsync(null!), () => c.Homes.AddDocumentAsync(null!), () => c.Homes.RemoveDocumentAsync(null!),
            () => c.Homes.SubmitAsync(null!), () => c.Homes.ApproveAsync(null!), () => c.Homes.ReturnAsync(null!), () => c.Homes.SuspendAsync(null!),
            () => c.Homes.ReopenAsync(null!), () => c.Homes.EndAsync(null!), () => c.Homes.SetCancellationPolicyAsync(null!), () => c.Homes.DisableCancellationPolicyAsync(null!) ];
        Assert.Equal(20, writes.Length); foreach (var write in writes) await Assert.ThrowsAsync<ForbiddenException>(write);
    }
}
