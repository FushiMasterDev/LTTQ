using System.Text.Json;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Microsoft.Data.SqlClient;
namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M41ContractReadTests
{
    private static Task<IReadOnlyList<T>> Rows<T>(M21TestClient c, string sql, Func<SqlDataReader, T> map, Action<SqlCommand>? parameters = null) =>
        c.Factory.ReadAsync(async (db, ct) => { using var cmd = db.CreateCommand(sql); parameters?.Invoke(cmd); return (IReadOnlyList<T>)await db.QueryAsync(cmd, map, ct); }, CancellationToken.None);
    private static Task<int> SeedHome(M21TestClient c) => c.ScalarAsync<int>("SELECT Id FROM dbo.Homestays WHERE Name=N'Nhà Mẫu Sen Tây Hồ';");

    [SqlFact]
    public async Task SeedProfilesOwnersContractsAndLookupsMatchDatabase()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var home = await SeedHome(c.Base);
        var list = await c.Homes.ListAsync(new() { Page = new() { SearchText = "Nhà Mẫu", PageSize = 10, SortColumn = "Name" } });
        Assert.Equal(6, list.TotalCount); Assert.Equal(6, list.Items.Count); Assert.Equal(3, list.Items.Select(x => x.AreaId).Distinct().Count());
        var detail = await c.Homes.GetAsync(home);
        Assert.Equal("Nhà Mẫu Sen Tây Hồ", detail.Homestay.Name); Assert.Equal(2, detail.RoomTypes.Count); Assert.Equal(6, detail.Rooms.Count);
        Assert.Equal(2, detail.Homestay.RoomTypeCount); Assert.Equal(6, detail.Homestay.RoomCount); Assert.Single(detail.Images); Assert.Equal(2, detail.Documents.Count);
        Assert.True(detail.CanViewLegalDocuments); Assert.True(detail.CanViewContracts); Assert.Single(detail.Contracts); Assert.Equal(.10m, detail.Homestay.CommissionRate);
        Assert.Single(detail.CancellationPolicies); Assert.Equal(new[] { 10, 5, 0 }, detail.CancellationPolicies[0].Rules.Select(x => x.MinDaysBefore));
        Assert.Equal(detail.CancellationPolicies.Select(x => x.Id), (await c.Homes.ListCancellationPoliciesAsync(home)).Select(x => x.Id));
        Assert.Equal(detail.RoomTypes.Select(x => x.Id), (await c.Rooms.ListTypesAsync(home)).Select(x => x.Id));
        Assert.Equal(detail.Rooms.Select(x => x.Id), (await c.Rooms.ListRoomsAsync(home)).Select(x => x.Id));
        Assert.Equal(detail.Homestay.RowVer, detail.RoomTypes[0].RowVer); Assert.Equal(detail.Homestay.RowVer, detail.Contracts[0].RowVer);
        Assert.Equal(32, detail.Owner.RowVer.Length); Assert.Equal(8, detail.Homestay.RowVer.Length);
        var owner = await c.Owners.GetAsync(detail.Owner.Id); Assert.False(owner.IsSensitiveMasked); Assert.Equal("000000000001", owner.BankAccountNo);
        var owners = await c.Owners.ListAsync(new() { Status = OwnerStatus.Approved, Page = new() { SearchText = "Mẫu", SortColumn = "FullName" } });
        Assert.Equal(3, owners.TotalCount); Assert.All(owners.Items, x => Assert.Equal(OwnerStatus.Approved, x.Status));
        Assert.Contains(await c.Owners.LookupAsync(), x => x.Id == owner.Id && x.IsActive);
        Assert.Equal(await c.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Homestays;"), (await c.Homes.ListHomestaysAsync()).Count);
        Assert.Equal(await c.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Areas;"), (await c.Homes.ListAreasAsync()).Count);
        Assert.Equal(await c.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Amenities WHERE IsActive=1;"), (await c.Homes.ListAmenitiesAsync()).Count);
        Assert.Equal(await c.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RoomCategories WHERE IsActive=1;"), (await c.Homes.ListRoomCategoriesAsync()).Count);
        var contracts = await c.Contracts.ListAsync(new() { Status = ContractStatus.Active, Page = new() { SearchText = "HD-MAU-", SortColumn = "ContractNo" } });
        Assert.Equal(4, contracts.TotalCount); Assert.Equal(new[] { .10m, .12m, .15m, .11m }, contracts.Items.Select(x => x.CommissionRate));
        Assert.Equal(detail.Contracts[0].ContractNo, (await c.Contracts.GetAsync(detail.Contracts[0].Id)).ContractNo);
        Assert.Single((await c.Contracts.ListAsync(new() { HomestayId = home })).Items);
        Assert.Equal((await c.Homes.ListHomestaysAsync()).Select(x => x.Id), (await c.Contracts.ListHomestaysAsync()).Select(x => x.Id));
        Assert.Single((await c.Homes.ListAsync(new() { PendingReviewOnly = true, Page = new() { SearchText = "Nhà Mẫu" } })).Items);
    }
    [SqlFact]
    public async Task ProfilePagingFiltersAndLiteralSearchAreDoneBeforeReturningThePage()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var d = await M21Data.CreateAsync(c.Base);
        for (var i = 0; i < 12; i++) await c.Base.MutateAsync("""
            INSERT dbo.Homestays(OwnerId,AreaId,Name,Address,Status,CreatedByUserId)
            SELECT OwnerId,AreaId,@name,N'Test','Draft',CreatedByUserId FROM dbo.Homestays WHERE Id=@id;
            INSERT dbo.Contracts(HomestayId,ContractNo,StartDate,EndDate,CommissionRate,Status,SignedByEmployeeId)
            VALUES(@id,@number,'2020-01-01','2021-01-01',.12,'Expired',@employee);
            INSERT dbo.Owners(FullName,Phone,Status,CreatedByEmployeeId) VALUES(@name,@phone,'Pending',@employee);
            """, cmd =>
        {
            cmd.Parameters.AddNVarChar("@name", 100, $"M41 paging {d.HomestayId} {i:00}"); cmd.Parameters.AddInt32("@id", d.HomestayId);
            cmd.Parameters.AddVarChar("@number", 30, $"M41-{d.HomestayId}-{i:00}"); cmd.Parameters.AddVarChar("@phone", 16, M14TestClient.Phone());
            cmd.Parameters.AddInt32("@employee", c.Base.Current.RequireSession().EmployeeId);
        });
        var q = new HomestaySearchRequest { AreaId = d.AreaId, Status = HomestayStatus.Draft, Page = new() { PageSize = 10, SortColumn = "Name", SearchText = $"M41 paging {d.HomestayId} " } };
        var first = await c.Homes.ListAsync(q); var second = await c.Homes.ListAsync(q with { Page = q.Page with { PageNumber = 2 } });
        Assert.Equal(12, first.TotalCount); Assert.Equal(10, first.Items.Count); Assert.Equal(2, second.Items.Count); Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        var ownerQuery = new Homestay.DTO.Owners.OwnerSearchRequest { Status = OwnerStatus.Pending, Page = q.Page with { SortColumn = "FullName" } };
        var ownersFirst = await c.Owners.ListAsync(ownerQuery); var ownersSecond = await c.Owners.ListAsync(ownerQuery with { Page = ownerQuery.Page with { PageNumber = 2 } });
        Assert.Equal(12, ownersFirst.TotalCount); Assert.Equal(10, ownersFirst.Items.Count); Assert.Equal(2, ownersSecond.Items.Count);
        Assert.Empty(ownersFirst.Items.Select(x => x.Id).Intersect(ownersSecond.Items.Select(x => x.Id)));
        Assert.Single((await c.Owners.ListAsync(new() { Page = new() { SearchText = ownersFirst.Items[0].Phone } })).Items);
        var contractQuery = new ContractSearchRequest { HomestayId = d.HomestayId, Status = ContractStatus.Expired, Page = new() { PageSize = 10, SortColumn = "ContractNo", SearchText = $"M41-{d.HomestayId}-" } };
        var contractsFirst = await c.Contracts.ListAsync(contractQuery); var contractsSecond = await c.Contracts.ListAsync(contractQuery with { Page = contractQuery.Page with { PageNumber = 2 } });
        Assert.Equal(12, contractsFirst.TotalCount); Assert.Equal(10, contractsFirst.Items.Count); Assert.Equal(2, contractsSecond.Items.Count);
        Assert.Empty(contractsFirst.Items.Select(x => x.Id).Intersect(contractsSecond.Items.Select(x => x.Id)));
        Assert.Empty((await c.Homes.ListAsync(q with { Status = HomestayStatus.Active })).Items);
        Assert.Empty((await c.Homes.ListAsync(q with { OwnerId = int.MaxValue })).Items);
        Assert.Empty((await c.Homes.ListAsync(q with { Page = q.Page with { SearchText = "%_[" } })).Items);
        Assert.Empty((await c.Owners.ListAsync(new() { Page = new() { SearchText = "%_[" } })).Items);
        Assert.Empty((await c.Contracts.ListAsync(new() { Page = new() { SearchText = "%_[" } })).Items);
        await Assert.ThrowsAsync<BusinessException>(() => c.Homes.ListAsync(q with { Page = q.Page with { SortColumn = "Name;DELETE" } }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Contracts.ListAsync(new() { Status = (ContractStatus)999 }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Owners.ListAsync(new() { Status = (OwnerStatus)999 }));
    }
    [SqlFact]
    public async Task StaffAndAreaManagersCannotReadProfilesOutsideAreaAndAccountantHasOnlyFinancialReads()
    {
        var manager = new M41TestClient(); await manager.Base.LoginNewAsync(); var home = await SeedHome(manager.Base); var detail = await manager.Homes.GetAsync(home);
        foreach (var role in new[] { Position.Staff, Position.Manager })
        {
            var outside = new M41TestClient(); await outside.Base.LoginNewAsync(role, allAreas: false);
            Assert.Empty((await outside.Homes.ListAsync(new())).Items); Assert.Empty(await outside.Homes.ListHomestaysAsync()); Assert.Empty(await outside.Homes.ListAreasAsync());
            Assert.Empty((await outside.Owners.ListAsync(new())).Items); Assert.Empty(await outside.Owners.LookupAsync());
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Homes.GetAsync(home)); await Assert.ThrowsAsync<NotFoundException>(() => outside.Homes.GetApprovalConditionsAsync(home));
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Homes.ListCancellationPoliciesAsync(home)); await Assert.ThrowsAsync<NotFoundException>(() => outside.Owners.GetAsync(detail.Owner.Id));
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.ListTypesAsync(home)); await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.ListRoomsAsync(home));
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.ListPricesAsync(home));
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Rooms.ListBlocksAsync(new(home, new(2026, 1, 1), new(2099, 1, 1))));
            await Assert.ThrowsAsync<NotFoundException>(() => outside.Files.GetHomestayImageAsync(home, detail.Images[0].File.FileId));
            if (role == Position.Manager) { Assert.Empty((await outside.Contracts.ListAsync(new())).Items); await Assert.ThrowsAsync<NotFoundException>(() => outside.Contracts.GetAsync(detail.Contracts[0].Id)); }
        }
        var staff = new M41TestClient(); await staff.Base.LoginNewAsync(Position.Staff, detail.Homestay.AreaId, allAreas: false);
        var staffHome = await staff.Homes.GetAsync(home); Assert.Null(staffHome.Homestay.CommissionRate); Assert.Empty(staffHome.Contracts); Assert.Empty(staffHome.Documents);
        Assert.False(staffHome.CanViewContracts); Assert.False(staffHome.CanViewLegalDocuments); Assert.Contains("•", staffHome.Owner.IdNumber!); Assert.Contains("•", staffHome.Owner.BankAccountNo!);
        Assert.All((await staff.Homes.ListAsync(new())).Items, x => Assert.Equal(detail.Homestay.AreaId, x.AreaId));
        Assert.Contains("•", (await staff.Owners.GetAsync(detail.Owner.Id)).BankAccountNo!);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Contracts.GetAsync(detail.Contracts[0].Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Files.GetHomestayDocumentAsync(home, detail.Documents[0].File.FileId));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Homes.ListAsync(new() { PendingReviewOnly = true }));
        var accountant = new M41TestClient(); await accountant.Base.LoginNewAsync(Position.Accountant);
        var accountingOwner = await accountant.Owners.GetAsync(detail.Owner.Id); Assert.Contains("•", accountingOwner.IdNumber!); Assert.Equal(detail.Owner.BankAccountNo, accountingOwner.BankAccountNo);
        Assert.Equal(detail.Contracts[0].CommissionRate, (await accountant.Contracts.GetAsync(detail.Contracts[0].Id)).CommissionRate);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Homes.GetAsync(home)); await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Rooms.ListTypesAsync(home));
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Files.GetHomestayImageAsync(home, detail.Images[0].File.FileId));
    }
    [SqlFact]
    public async Task NewPendingOwnerIsVisibleToItsCreatorWithoutAnAreaAndSnapshotChangesWithBankData()
    {
        var c = new M41TestClient(); var identity = await c.Base.LoginNewAsync(Position.Staff, allAreas: false);
        var id = await c.Base.ScalarAsync<int>("INSERT dbo.Owners(FullName,Phone,Status,CreatedByEmployeeId,BankAccountNo,IdNumber) OUTPUT inserted.Id VALUES(N'M41 pending',@phone,'Pending',@employee,'1234567890','123456789012');", cmd =>
        { cmd.Parameters.AddVarChar("@phone", 16, M14TestClient.Phone()); cmd.Parameters.AddInt32("@employee", identity.EmployeeId); });
        var old = await c.Owners.GetAsync(id); Assert.Contains(await c.Owners.LookupAsync(), x => x.Id == id && !x.IsActive);
        await c.Base.MutateAsync("UPDATE dbo.Owners SET BankAccountNo='00009999' WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", id));
        var next = await c.Owners.GetAsync(id); Assert.False(old.RowVer.SequenceEqual(next.RowVer)); Assert.Equal("••••9999", next.BankAccountNo);
        var other = new M41TestClient(); await other.Base.LoginNewAsync(Position.Staff, allAreas: false); await Assert.ThrowsAsync<NotFoundException>(() => other.Owners.GetAsync(id));
    }
    [SqlFact]
    public async Task ActiveSeedPassesAllNineApprovalConditionsAndEmptyDraftReportsExactlyTheMissingOnes()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync();
        var active = await Rows(c.Base, "SELECT Id FROM dbo.Homestays WHERE Name LIKE N'Nhà Mẫu %' AND Status='Active';", r => r.GetInt32("Id")); Assert.Equal(4, active.Count);
        foreach (var id in active) { var checklist = await c.Homes.GetApprovalConditionsAsync(id); Assert.True(checklist.CanApprove); Assert.Equal(9, checklist.Conditions.Count); Assert.All(checklist.Conditions, x => Assert.True(x.IsSatisfied, x.Message)); }
        var d = await M21Data.CreateAsync(c.Base);
        await c.Base.MutateAsync("UPDATE dbo.Homestays SET Status='Draft',Address=N' ',ContactPhone=NULL,Description=NULL WHERE Id=@id;UPDATE dbo.RoomTypes SET IsActive=0 WHERE HomestayId=@id;", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        var missing = await c.Homes.GetApprovalConditionsAsync(d.HomestayId);
        Assert.False(missing.CanApprove); Assert.Equal(new[] { "OwnerApproved", "ActiveContract", "BusinessLicense", "FireSafety", "SellableRoom", "CoverImage", "Address", "ContactPhone", "Description" }, missing.Conditions.Where(x => !x.IsSatisfied).Select(x => x.Code));
        Assert.All(missing.Conditions, x => Assert.False(string.IsNullOrWhiteSpace(x.Message))); Assert.Equal(DateOnly.FromDateTime(missing.ServerNowUtc.AddHours(7)), missing.Today);
    }
    [SqlFact]
    public async Task ApprovalUsesLatestEffectiveLevelsNullExpiryAndSupersededDocumentsCorrectly()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var d = await M21Data.CreateAsync(c.Base);
        await MakeReady(c, d);
        Assert.True((await c.Homes.GetApprovalConditionsAsync(d.HomestayId)).CanApprove);
        await c.Base.MutateAsync("UPDATE dbo.VerificationDocuments SET IsSuperseded=1 WHERE HomestayId=@id AND DocumentType='BusinessLicense';UPDATE dbo.VerificationDocuments SET ExpiryDate=DATEADD(day,-1,CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) WHERE HomestayId=@id AND DocumentType='FireSafety';", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        var check = await c.Homes.GetApprovalConditionsAsync(d.HomestayId); Assert.Equal(new[] { "BusinessLicense", "FireSafety" }, check.Conditions.Where(x => !x.IsSatisfied).Select(x => x.Code));
        await c.Base.MutateAsync("UPDATE dbo.VerificationDocuments SET IsSuperseded=0,ExpiryDate=NULL WHERE HomestayId=@id;UPDATE dbo.RoomTypePrices SET EffectiveFrom=DATEADD(day,1,CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()))) WHERE RoomTypeId=@type AND DayType='Holiday';", cmd => { cmd.Parameters.AddInt32("@id", d.HomestayId); cmd.Parameters.AddInt32("@type", d.RoomTypeId); });
        Assert.Equal("SellableRoom", Assert.Single((await c.Homes.GetApprovalConditionsAsync(d.HomestayId)).Conditions, x => !x.IsSatisfied).Code);
    }
    private static Task MakeReady(M41TestClient c, M21Data d) => c.Base.MutateAsync("""
        DECLARE @today date=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME()));
        UPDATE dbo.Homestays SET OwnerId=(SELECT OwnerId FROM dbo.Homestays WHERE Name=N'Nhà Mẫu Sen Tây Hồ'),Description=N'Đủ hồ sơ' WHERE Id=@id;
        UPDATE dbo.Contracts SET StartDate=DATEADD(day,-1,@today),EndDate=DATEADD(year,1,@today) WHERE HomestayId=@id;
        UPDATE dbo.RoomTypePrices SET EffectiveFrom=@today WHERE RoomTypeId=@type;
        INSERT dbo.HomestayImages(HomestayId,FileId,IsCover) SELECT @id,i.FileId,1 FROM dbo.HomestayImages i JOIN dbo.Homestays h ON h.Id=i.HomestayId WHERE h.Name=N'Nhà Mẫu Sen Tây Hồ' AND i.IsCover=1;
        INSERT dbo.VerificationDocuments(HomestayId,DocumentType,FileId,ExpiryDate,UploadedByUserId)
        SELECT @id,d.DocumentType,d.FileId,NULL,@user FROM dbo.VerificationDocuments d JOIN dbo.Homestays h ON h.Id=d.HomestayId WHERE h.Name=N'Nhà Mẫu Sen Tây Hồ' AND d.IsSuperseded=0;
        """, cmd => { cmd.Parameters.AddInt32("@id", d.HomestayId); cmd.Parameters.AddInt32("@type", d.RoomTypeId); cmd.Parameters.AddInt32("@user", c.Base.Current.RequireSession().UserId); });
    [SqlFact]
    public async Task AllowedActionsCoverAllStatesRolesAndCreatorSeparation()
    {
        var creator = new M41TestClient(); await creator.Base.LoginNewAsync(); var d = await M21Data.CreateAsync(creator.Base); await MakeReady(creator, d);
        // End is initially blocked by the real active contract; later exercise the unblocked branch too.
        var otherManager = new M41TestClient(); await otherManager.Base.LoginNewAsync();
        var staff = new M41TestClient(); await staff.Base.LoginNewAsync(Position.Staff, d.AreaId, allAreas: false);
        var accountant = new M41TestClient(); await accountant.Base.LoginNewAsync(Position.Accountant);
        foreach (var status in Enum.GetValues<HomestayStatus>())
        {
            await creator.Base.MutateAsync("UPDATE dbo.Homestays SET Status=@status WHERE Id=@id;", cmd => { cmd.Parameters.AddEnum("@status", 20, (HomestayStatus?)status); cmd.Parameters.AddInt32("@id", d.HomestayId); });
            foreach (var viewer in new[] { creator, otherManager, staff })
            {
                var detail = await viewer.Homes.GetAsync(d.HomestayId); var actions = detail.AllowedActions; var manager = viewer != staff;
                foreach (var action in new[] { HomestayAction.Edit, HomestayAction.SetAmenities, HomestayAction.ManageImages, HomestayAction.ManageDocuments, HomestayAction.ManageRooms, HomestayAction.AddPrice, HomestayAction.ManageCancellationPolicy })
                    Assert.Equal(manager || status == HomestayStatus.Draft, actions.Contains(action));
                Assert.Equal(status == HomestayStatus.Draft, actions.Contains(HomestayAction.Submit));
                Assert.Equal(manager && viewer != creator && status == HomestayStatus.PendingReview, actions.Contains(HomestayAction.Approve));
                Assert.Equal(manager && status == HomestayStatus.PendingReview, actions.Contains(HomestayAction.Return));
                Assert.Equal(manager && status == HomestayStatus.Active, actions.Contains(HomestayAction.Suspend));
                Assert.Equal(manager && status == HomestayStatus.Suspended, actions.Contains(HomestayAction.Reopen));
                Assert.Equal(manager, actions.Contains(HomestayAction.BlockRoom)); Assert.DoesNotContain(HomestayAction.End, actions);
                Assert.Equal(manager || status == HomestayStatus.Draft, detail.CanViewLegalDocuments);
                if (viewer == staff && status == HomestayStatus.Draft)
                    Assert.NotEmpty((await staff.Files.GetHomestayDocumentAsync(d.HomestayId, detail.Documents[0].File.FileId)).Content);
            }
            await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Homes.GetAsync(d.HomestayId));
        }
        await creator.Base.MutateAsync("UPDATE dbo.Contracts SET Status='Expired' WHERE HomestayId=@id;UPDATE dbo.Homestays SET Status='Draft' WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        Assert.Contains(HomestayAction.End, (await creator.Homes.GetAsync(d.HomestayId)).AllowedActions);
        _ = await d.BookingAsync(creator.Base, 0, BookingStatus.Confirmed);
        Assert.DoesNotContain(HomestayAction.End, (await creator.Homes.GetAsync(d.HomestayId)).AllowedActions);
        await creator.Base.MutateAsync("UPDATE dbo.Bookings SET Status='Completed',CheckedInAt=SYSUTCDATETIME(),CompletedAt=SYSUTCDATETIME() WHERE HomestayId=@id;", cmd => cmd.Parameters.AddInt32("@id", d.HomestayId));
        Assert.Contains(HomestayAction.End, (await creator.Homes.GetAsync(d.HomestayId)).AllowedActions);
    }
    [SqlFact]
    public async Task PriceVersionsHaveThreeLevelsAndUseServerVietnamDate()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var home = await SeedHome(c.Base); var prices = await c.Rooms.ListPricesAsync(home);
        Assert.Equal(DateOnly.FromDateTime(prices.ServerNowUtc.AddHours(7)), prices.Today); Assert.Equal(4, prices.Versions.Count);
        var sql = await Rows(c.Base, "SELECT p.RoomTypeId,p.EffectiveFrom,p.DayType,p.Price FROM dbo.RoomTypePrices p JOIN dbo.RoomTypes t ON t.Id=p.RoomTypeId WHERE t.HomestayId=@id;", r => (Type: r.GetInt32("RoomTypeId"), Date: r.GetDateOnly("EffectiveFrom"), Day: r.GetString("DayType"), Price: r.GetDecimal("Price")), cmd => cmd.Parameters.AddInt32("@id", home));
        foreach (var version in prices.Versions)
        {
            var levels = sql.Where(x => x.Type == version.RoomTypeId && x.Date == version.EffectiveFrom).ToDictionary(x => x.Day, x => x.Price);
            Assert.Equal(levels["Weekday"], version.WeekdayPrice); Assert.Equal(levels["Weekend"], version.WeekendPrice); Assert.Equal(levels["Holiday"], version.HolidayPrice);
            Assert.Equal(version.EffectiveFrom <= prices.Today, version.HasTakenEffect);
        }
        Assert.Equal(2, prices.Versions.Count(x => x.HasTakenEffect)); Assert.Equal(2, prices.Versions.Count(x => !x.HasTakenEffect));
        var d = await M21Data.CreateAsync(c.Base); await c.Base.MutateAsync("UPDATE dbo.RoomTypePrices SET EffectiveFrom=CONVERT(date,DATEADD(hour,7,SYSUTCDATETIME())) WHERE RoomTypeId=@id;", cmd => cmd.Parameters.AddInt32("@id", d.RoomTypeId));
        Assert.True(Assert.Single((await c.Rooms.ListPricesAsync(d.HomestayId)).Versions).HasTakenEffect);
    }
    [SqlFact]
    public async Task BlockIntervalsAndCalendarReuseTheSameSeedLedger()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var home = await SeedHome(c.Base);
        var seed = Assert.Single(await Rows(c.Base, "SELECT b.Id,b.RoomId,b.StartDate,b.EndDate FROM dbo.RoomBlocks b JOIN dbo.Rooms r ON r.Id=b.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId WHERE t.HomestayId=@id;", r => (Id: r.GetInt32("Id"), Room: r.GetInt32("RoomId"), Start: r.GetDateOnly("StartDate"), End: r.GetDateOnly("EndDate")), cmd => cmd.Parameters.AddInt32("@id", home)));
        var block = Assert.Single(await c.Rooms.ListBlocksAsync(new(home, seed.Start, seed.End))); Assert.Equal(seed.Id, block.Id); Assert.Equal(seed.Room, block.RoomId);
        Assert.Empty(await c.Rooms.ListBlocksAsync(new(home, seed.End, seed.End.AddDays(1)))); Assert.Empty(await c.Rooms.ListBlocksAsync(new(home, seed.Start.AddDays(-1), seed.Start)));
        var request = new Homestay.DTO.Availability.RoomCalendarRequest(home, null, seed.Start, 14);
        var existing = await c.Base.Availability.GetCalendarAsync(request); var delegated = await c.Rooms.GetCalendarAsync(request);
        Assert.Equal(JsonSerializer.Serialize(existing.Rooms), JsonSerializer.Serialize(delegated.Rooms));
        Assert.Equal(3, delegated.Rooms.Single(x => x.RoomId == block.RoomId).Cells.Count(x => x.RoomBlockId == block.Id));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.ListBlocksAsync(new(home, seed.Start, seed.Start)));
    }
    [SqlFact]
    public async Task FileMetadataAndContentMatchSeedAndDoNotAllowCrossHomestayFiles()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var home = await SeedHome(c.Base); var detail = await c.Homes.GetAsync(home);
        foreach (var metadata in detail.Images.Select(x => x.File).Concat(detail.Documents.Select(x => x.File)))
        {
            var isImage = detail.Images.Any(x => x.File.FileId == metadata.FileId);
            var file = isImage ? await c.Files.GetHomestayImageAsync(home, metadata.FileId) : await c.Files.GetHomestayDocumentAsync(home, metadata.FileId);
            Assert.Equal(metadata.FileName, file.FileName); Assert.Equal(metadata.ContentType, file.ContentType); Assert.Equal(metadata.SizeBytes, file.Content.Length);
            Assert.Equal(metadata.Sha256, System.Security.Cryptography.SHA256.HashData(file.Content));
        }
        var other = await c.Base.ScalarAsync<int>("SELECT Id FROM dbo.Homestays WHERE Name=N'Nhà Mẫu Nắng Ba Đình';");
        await Assert.ThrowsAsync<NotFoundException>(() => c.Files.GetHomestayImageAsync(other, detail.Images[0].File.FileId));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Files.GetHomestayDocumentAsync(other, detail.Documents[0].File.FileId));
        await Assert.ThrowsAsync<NotFoundException>(() => c.Files.GetHomestayImageAsync(home, detail.Documents[0].File.FileId));
    }
    [SqlFact]
    public async Task ProfileAuditIsOwnForStaffAndNeverReturnsRawSensitiveJson()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync(); var d = await M21Data.CreateAsync(c.Base);
        var staff = new M41TestClient(); var identity = await staff.Base.LoginNewAsync(Position.Staff, d.AreaId, allAreas: false);
        await c.Base.MutateAsync("""
            INSERT dbo.AuditLogs(UserId,ActionCode,TableName,RecordId,OldValues,NewValues,MachineName)
            VALUES(@user,'Homestay.Update','Homestays',CONVERT(varchar(50),@id),N'{"Status":"Draft","IdNumber":"secret-identity","BankAccountNo":"secret-account"}',N'{"Status":"Active","CommissionRate":0.12,"Content":"secret-bytes"}',N'M41');
            INSERT dbo.AuditLogs(UserId,ActionCode,TableName,RecordId,NewValues,MachineName)
            VALUES(@manager,'Homestay.Update','Homestays',CONVERT(varchar(50),@id),N'{"Status":"PendingReview"}',N'M41');
            """, cmd => { cmd.Parameters.AddInt32("@user", identity.UserId); cmd.Parameters.AddInt32("@manager", c.Base.Current.RequireSession().UserId); cmd.Parameters.AddInt32("@id", d.HomestayId); });
        var own = Assert.Single((await staff.Homes.GetAsync(d.HomestayId)).Audit); Assert.DoesNotContain("secret", own.OldValues!); Assert.DoesNotContain("CommissionRate", own.NewValues!); Assert.DoesNotContain("Content", own.NewValues!);
        Assert.Equal(2, (await c.Homes.GetAsync(d.HomestayId)).Audit.Count);
    }
    [SqlFact]
    public async Task F08NewFiltersAndLookupsMatchDirectScopeCountsIncludingOwnOutsideAreaBooking()
    {
        var manager = new M41TestClient(); await manager.Base.LoginNewAsync(); var seed = await manager.Homes.GetAsync(await SeedHome(manager.Base));
        var staff = new M41TestClient(); var identity = await staff.Base.LoginNewAsync(Position.Staff, seed.Homestay.AreaId, allAreas: false);
        var own = await M21Data.CreateAsync(staff.Base); var ownBooking = await own.BookingAsync(staff.Base, 0);
        await Assert.ThrowsAsync<NotFoundException>(() => staff.Homes.GetAsync(own.HomestayId));
        void Parameters(SqlCommand cmd) { cmd.Parameters.AddInt32("@area", seed.Homestay.AreaId); cmd.Parameters.AddInt32("@employee", identity.EmployeeId); }
        const string scope = "(h.AreaId=@area OR b.CreatedByEmployeeId=@employee OR b.AssignedEmployeeId=@employee)";
        var expected = await Rows(staff.Base, $"SELECT b.Id,h.AreaId,b.AssignedEmployeeId FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE {scope};", r => (Id: r.GetInt32("Id"), Area: r.GetInt32("AreaId"), Employee: r.GetNullableInt32("AssignedEmployeeId")), Parameters);
        Assert.Contains(expected, x => x.Id == ownBooking);
        foreach (var area in expected.Select(x => x.Area).Distinct())
        {
            var page = await staff.Base.Bookings.ListAsync(new() { Queue = BookingQueue.All, AreaId = area }); Assert.Equal(expected.Count(x => x.Area == area), page.TotalCount); Assert.All(page.Items, x => Assert.Equal(area, x.AreaId));
            foreach (var employee in expected.Where(x => x.Area == area && x.Employee.HasValue).Select(x => x.Employee!.Value).Distinct())
            {
                var q = await staff.Base.Bookings.ListAsync(new() { Queue = BookingQueue.All, AreaId = area, AssignedEmployeeId = employee });
                Assert.Equal(expected.Count(x => x.Area == area && x.Employee == employee), q.TotalCount); Assert.All(q.Items, x => Assert.Equal(employee, x.AssignedEmployeeId));
            }
        }
        Assert.Equal(expected.Count(x => x.Employee == identity.EmployeeId), (await staff.Base.Bookings.ListAsync(new() { Queue = BookingQueue.All, AssignedEmployeeId = identity.EmployeeId })).TotalCount);
        var expectedHomes = await Rows(staff.Base, $"SELECT h.Id FROM dbo.Homestays h WHERE h.AreaId=@area OR EXISTS(SELECT 1 FROM dbo.Bookings b WHERE b.HomestayId=h.Id AND {scope}) ORDER BY h.Name,h.Id;", r => r.GetInt32("Id"), Parameters);
        Assert.Equal(expectedHomes, (await staff.Base.Bookings.ListFilterHomestaysAsync()).Select(x => x.Id));
        var expectedAreas = await Rows(staff.Base, $"SELECT a.Id FROM dbo.Areas a WHERE EXISTS(SELECT 1 FROM dbo.Homestays h WHERE h.AreaId=a.Id AND (h.AreaId=@area OR EXISTS(SELECT 1 FROM dbo.Bookings b WHERE b.HomestayId=h.Id AND {scope}))) ORDER BY a.Name,a.Id;", r => r.GetInt32("Id"), Parameters);
        Assert.Equal(expectedAreas, (await staff.Base.Bookings.ListFilterAreasAsync()).Select(x => x.Id));
        Assert.Equal(expected.Where(x => x.Employee.HasValue).Select(x => x.Employee!.Value).Distinct().Order(), (await staff.Base.Bookings.ListFilterAssigneesAsync()).Select(x => x.Id).Order());
        Assert.All(await staff.Base.Bookings.ListFilterHomestaysAsync(own.AreaId), x => Assert.Equal(own.HomestayId, x.Id));
        Assert.Single(await staff.Base.Bookings.ListFilterAssigneesAsync(own.AreaId, own.HomestayId));
        Assert.Empty(await staff.Base.Bookings.ListFilterAssigneesAsync(homestayId: int.MaxValue));
        var outside = new M41TestClient(); await outside.Base.LoginNewAsync(Position.Staff, allAreas: false);
        Assert.Empty(await outside.Base.Bookings.ListFilterHomestaysAsync()); Assert.Empty(await outside.Base.Bookings.ListFilterAreasAsync()); Assert.Empty(await outside.Base.Bookings.ListFilterAssigneesAsync());
        var code = await staff.Base.ScalarAsync<string>("SELECT Code FROM dbo.Bookings WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", ownBooking));
        Assert.Equal(0, (await outside.Base.Bookings.ListAsync(new() { Code = code, AreaId = own.AreaId })).TotalCount);
        Assert.Equal(1, (await outside.Base.Bookings.ListAsync(new() { Code = code })).TotalCount);
        var accountant = new M41TestClient(); await accountant.Base.LoginNewAsync(Position.Accountant);
        Assert.Equal(await accountant.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Homestays;"), (await accountant.Base.Bookings.ListFilterHomestaysAsync()).Count);
        Assert.Equal(await accountant.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE h.AreaId=@area;", cmd => cmd.Parameters.AddInt32("@area", own.AreaId)), (await accountant.Base.Bookings.ListAsync(new() { Queue = BookingQueue.All, AreaId = own.AreaId })).TotalCount);
    }
    [SqlFact]
    public async Task AllThirtyFiveM41WritesAreExplicitNotImplementedContracts()
    {
        var c = new M41TestClient(); await c.Base.LoginNewAsync();
        var writes = new Func<Task>[] {
            ()=>c.Owners.CreateAsync(null!),
            ()=>c.Owners.UpdateAsync(null!),
            ()=>c.Owners.ChangeBankAsync(null!),
            ()=>c.Owners.ChangeStatusAsync(null!),
            ()=>c.Homes.CreateDraftAsync(null!),
            ()=>c.Homes.UpdateAsync(null!),
            ()=>c.Homes.SetAmenitiesAsync(null!),
            ()=>c.Homes.AddImageAsync(null!),
            ()=>c.Homes.UpdateImageAsync(null!),
            ()=>c.Homes.RemoveImageAsync(null!),
            ()=>c.Homes.AddDocumentAsync(null!),
            ()=>c.Homes.RemoveDocumentAsync(null!),
            ()=>c.Homes.SubmitAsync(null!),
            ()=>c.Homes.ApproveAsync(null!),
            ()=>c.Homes.ReturnAsync(null!),
            ()=>c.Homes.SuspendAsync(null!),
            ()=>c.Homes.ReopenAsync(null!),
            ()=>c.Homes.EndAsync(null!),
            ()=>c.Homes.SetCancellationPolicyAsync(null!),
            ()=>c.Homes.DisableCancellationPolicyAsync(null!),
            ()=>c.Rooms.CreateTypeAsync(null!),
            ()=>c.Rooms.UpdateTypeAsync(null!),
            ()=>c.Rooms.SetTypeActiveAsync(null!),
            ()=>c.Rooms.CreateRoomAsync(null!),
            ()=>c.Rooms.UpdateRoomAsync(null!),
            ()=>c.Rooms.SetRoomActiveAsync(null!),
            ()=>c.Rooms.AddPriceAsync(null!),
            ()=>c.Rooms.UpdatePriceAsync(null!),
            ()=>c.Rooms.BlockAsync(null!),
            ()=>c.Rooms.UnblockAsync(null!),
            ()=>c.Contracts.CreateAsync(null!),
            ()=>c.Contracts.ActivateAsync(null!),
            ()=>c.Contracts.TerminateAsync(null!),
            ()=>c.Contracts.ExtendAsync(null!),
            ()=>c.Base.Payments.UpdateRefundAccountAsync(null!),
        };
        Assert.Equal(35, writes.Length);
        foreach (var write in writes) await Assert.ThrowsAsync<NotImplementedException>(write);
    }
}
