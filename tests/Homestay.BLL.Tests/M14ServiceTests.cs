using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.BLL.Tests;

public sealed class M14ServiceTests
{
    [SqlFact]
    public async Task LoginTrimsCodeIgnoresCaseAndCreatesOnlyOneActiveSession()
    {
        var client = new M14TestClient();
        var identity = await client.CreateIdentityAsync();
        var session = await client.Auth.LoginAsync(new() { Username = "  " + identity.Code.ToLowerInvariant() + "  ", Password = identity.Password, MachineName = client.Machine });
        Assert.Equal(identity.UserId, session.UserId);
        Assert.Equal(DateTimeKind.Utc, session.LoginAtUtc.Kind);
        Assert.True(await client.Sessions.HeartbeatAsync());
        Assert.Equal(1, await client.ReadAsync("SELECT COUNT(*) AS N FROM dbo.UserSessions WHERE UserId=@id AND EndedAt IS NULL;", r => r.GetInt32("N"), identity.UserId));
        await client.Sessions.LogoutAsync();
        await client.Sessions.LogoutAsync();
        Assert.Null(client.Current.Session);
        Assert.False(await client.Sessions.HeartbeatAsync());
    }

    [SqlFact]
    public async Task FiveFailuresPersistLockoutAndAuditWithoutPasswords()
    {
        var client = new M14TestClient();
        var identity = await client.CreateIdentityAsync();
        for (var i = 0; i < 5; i++)
            Assert.Equal(LoginErrors.InvalidCredentials, (await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(identity, "WrongSecret789"))).Message);
        var locked = await client.ReadAsync("SELECT FailedLoginCount, LockoutEndUtc FROM dbo.Users WHERE Id=@id;",
            r => (Count: r.GetInt32("FailedLoginCount"), End: r.GetNullableUtcDateTime("LockoutEndUtc")), identity.UserId);
        Assert.Equal(0, locked.Count);
        Assert.NotNull(locked.End);
        var error = await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(identity));
        Assert.StartsWith("Tài khoản tạm khóa do nhập sai nhiều lần.", error.Message);
        var audits = await client.Factory.ReadAsync(async (db, ct) =>
        {
            using var command = db.CreateCommand("SELECT RecordId, OldValues, NewValues, Note FROM dbo.AuditLogs WHERE UserId=@id AND ActionCode='Auth.LoginFailed';");
            command.Parameters.AddInt32("@id", identity.UserId);
            return await db.QueryAsync(command, r => (Record: r.GetString("RecordId"), Old: r.GetNullableString("OldValues"), New: r.GetNullableString("NewValues"), Note: r.GetNullableString("Note")), ct);
        }, CancellationToken.None);
        Assert.Equal(6, audits.Count);
        foreach (var audit in audits)
        {
            Assert.Equal(identity.Code, audit.Record);
            Assert.Null(audit.Old);
            Assert.Null(audit.New);
            Assert.Null(audit.Note);
        }
        await client.ExecuteAsync("UPDATE dbo.Users SET LockoutEndUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id;", identity.UserId);
        await client.LoginAsync(identity);
        Assert.Null(await client.ReadAsync("SELECT LockoutEndUtc FROM dbo.Users WHERE Id=@id;", r => r.GetNullableUtcDateTime("LockoutEndUtc"), identity.UserId));
    }

    [SqlFact]
    public async Task UnknownNameAndWrongPasswordUseSameErrorAndUnknownAuditHasNullUser()
    {
        var client = new M14TestClient();
        var identity = await client.CreateIdentityAsync();
        var wrong = await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(identity, "Wrong12345"));
        var unknownName = new string('X', 80);
        var unknown = await Assert.ThrowsAsync<BusinessException>(() => client.Auth.LoginAsync(new() { Username = unknownName, Password = "Secret12345", MachineName = client.Machine }));
        Assert.Equal(wrong.Message, unknown.Message);
        var audit = await client.Factory.ReadAsync(async (db, ct) =>
        {
            using var command = db.CreateCommand("SELECT UserId, RecordId, NewValues FROM dbo.AuditLogs WHERE MachineName=@machine AND UserId IS NULL AND ActionCode='Auth.LoginFailed';");
            command.Parameters.AddNVarChar("@machine", 100, client.Machine);
            return Assert.Single(await db.QueryAsync(command, r => (User: r.GetNullableInt32("UserId"), Record: r.GetString("RecordId"), Values: r.GetNullableString("NewValues")), ct));
        }, CancellationToken.None);
        Assert.Null(audit.User);
        Assert.Equal(unknownName[..50], audit.Record);
        Assert.Null(audit.Values);
    }

    [SqlFact]
    public async Task LegacyHashIsUpgradedWithoutChangingPasswordDateOrStamp()
    {
        const string oldHash = "PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==$rJD1TYj3W5Sdwgvkmwbmc0j4zOZYTc2x3MKZ2K4gxxA=";
        var client = new M14TestClient();
        var identity = await client.CreateIdentityAsync(hash: oldHash, password: "MậtKhẩu123");
        var before = await client.ReadAsync("SELECT SecurityStamp, PasswordChangedAt FROM dbo.Users WHERE Id=@id;",
            r => (Stamp: r.GetGuid("SecurityStamp"), Date: r.GetNullableUtcDateTime("PasswordChangedAt")), identity.UserId);
        await client.LoginAsync(identity);
        var after = await client.ReadAsync("SELECT PasswordHash, SecurityStamp, PasswordChangedAt FROM dbo.Users WHERE Id=@id;",
            r => (Hash: r.GetString("PasswordHash"), Stamp: r.GetGuid("SecurityStamp"), Date: r.GetNullableUtcDateTime("PasswordChangedAt")), identity.UserId);
        Assert.StartsWith("PBKDF2-SHA256$600000$", after.Hash);
        Assert.True(new PasswordHasher().Verify(identity.Password, after.Hash).IsValid);
        Assert.Equal(before.Stamp, after.Stamp);
        Assert.Equal(before.Date, after.Date);
    }

    [SqlFact]
    public async Task SecondLoginReplacesFirstAndRejectsItsNextWrite()
    {
        var a = new M14TestClient();
        var identity = await a.CreateIdentityAsync();
        await a.LoginAsync(identity);
        var b = new M14TestClient();
        await b.LoginAsync(identity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => a.Catalog.CreateItemAsync(CatalogKind.Amenity, new(M14TestClient.UniqueName())));
        Assert.False(await a.Sessions.HeartbeatAsync());
        Assert.True(await b.Sessions.HeartbeatAsync());
        Assert.Equal("Replaced", await a.ReadAsync("SELECT TOP(1) EndReason FROM dbo.UserSessions WHERE UserId=@id ORDER BY Id;", r => r.GetString("EndReason"), identity.UserId));
        await a.Sessions.LogoutAsync();
        Assert.True(await b.Sessions.HeartbeatAsync());
    }

    [SqlFact]
    public async Task ConcurrentLoginsLeaveExactlyOneOpenSession()
    {
        var a = new M14TestClient();
        var identity = await a.CreateIdentityAsync();
        var b = new M14TestClient();
        await Task.WhenAll(a.LoginAsync(identity), b.LoginAsync(identity));
        var valid = new[] { await a.Sessions.HeartbeatAsync(), await b.Sessions.HeartbeatAsync() };
        Assert.Single(valid, x => x);
        Assert.Equal(1, await a.ReadAsync("SELECT COUNT(*) AS N FROM dbo.UserSessions WHERE UserId=@id AND EndedAt IS NULL;", r => r.GetInt32("N"), identity.UserId));
    }

    [SqlFact]
    public async Task LockInvalidatesEmployeeSessionAndUnlockAllowsLogin()
    {
        var manager = new M14TestClient();
        await manager.LoginNewManagerAsync();
        var employee = await manager.CreateIdentityAsync(Position.Staff);
        var client = new M14TestClient();
        await client.LoginAsync(employee);
        var detail = await manager.Staff.GetAsync(employee.EmployeeId);
        await manager.Staff.LockAccountAsync(new() { UserId = employee.UserId, RowVer = detail.Employee.RowVer, Reason = "Khóa kiểm thử" });
        await Assert.ThrowsAsync<SessionExpiredException>(() => client.Sessions.ChangePasswordAsync(Change(employee.Password)));
        Assert.False(await client.Sessions.HeartbeatAsync());
        Assert.Equal(LoginErrors.InactiveAccount, (await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(employee))).Message);
        Assert.Equal(LoginErrors.InvalidCredentials, (await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(employee, "Wrong12345"))).Message);
        detail = await manager.Staff.GetAsync(employee.EmployeeId);
        await manager.Staff.UnlockAccountAsync(new() { UserId = employee.UserId, RowVer = detail.Employee.RowVer });
        await client.LoginAsync(employee);
        Assert.True(await client.Sessions.HeartbeatAsync());
    }

    [SqlFact]
    public async Task CreateTemporaryLoginChangeAndResetKeepOnlyAuthorizedSession()
    {
        var manager = new M14TestClient();
        await manager.LoginNewManagerAsync();
        var created = await manager.Staff.CreateAsync(new() { FullName = M14TestClient.UniqueName(), Phone = M14TestClient.Phone(), Position = Position.Manager, Email = "  " });
        Assert.StartsWith("NV", created.EmployeeCode);
        var identity = new TestIdentity(created.UserId, created.EmployeeId, created.EmployeeCode, created.TemporaryPassword);
        var a = new M14TestClient();
        var first = await a.LoginAsync(identity);
        Assert.True(first.MustChangePassword);
        var b = new M14TestClient();
        await b.LoginAsync(identity);
        var oldStamp = b.Current.RequireSession().SecurityStamp;
        await b.Sessions.ChangePasswordAsync(Change(identity.Password));
        Assert.False(b.Current.RequireSession().MustChangePassword);
        Assert.NotEqual(oldStamp, b.Current.RequireSession().SecurityStamp);
        await b.Catalog.CreateItemAsync(CatalogKind.Amenity, new(M14TestClient.UniqueName()));
        await Assert.ThrowsAsync<SessionExpiredException>(() => a.Sessions.ChangePasswordAsync(Change(identity.Password)));
        var detail = await manager.Staff.GetAsync(created.EmployeeId);
        Assert.Null(detail.Email);
        Assert.False(detail.MustChangePassword);
        var reset = await manager.Staff.ResetPasswordAsync(new() { UserId = created.UserId, RowVer = detail.Employee.RowVer });
        await Assert.ThrowsAsync<SessionExpiredException>(() => b.Catalog.CreateItemAsync(CatalogKind.Amenity, new(M14TestClient.UniqueName())));
        var next = await a.LoginAsync(identity, reset.TemporaryPassword);
        Assert.True(next.MustChangePassword);
        var audits = await manager.ReadAsync("""
            SELECT STRING_AGG(CONVERT(nvarchar(max), COALESCE(OldValues,N'')+COALESCE(NewValues,N'')),N' ') AS Payload
            FROM dbo.AuditLogs WHERE (TableName='Users' AND RecordId=CONVERT(varchar(50),@id))
                OR (TableName='Employees' AND RecordId=CONVERT(varchar(50),(SELECT Id FROM dbo.Employees WHERE UserId=@id)));
            """, r => r.GetString("Payload"), created.UserId);
        Assert.DoesNotContain(created.TemporaryPassword, audits);
        Assert.DoesNotContain(reset.TemporaryPassword, audits);
        Assert.DoesNotContain("PBKDF2", audits);
    }

    [SqlFact]
    public async Task ManagerCannotLockOrResetSelfAndStaleAccountVersionIsRejected()
    {
        var client = new M14TestClient();
        var manager = await client.LoginNewManagerAsync();
        var self = await client.Staff.GetAsync(manager.EmployeeId);
        var request = new AccountActionRequest { UserId = manager.UserId, RowVer = self.Employee.RowVer, Reason = "Self" };
        await Assert.ThrowsAsync<BusinessException>(() => client.Staff.LockAccountAsync(request));
        await Assert.ThrowsAsync<BusinessException>(() => client.Staff.ResetPasswordAsync(request));
        var target = await client.CreateIdentityAsync(Position.Staff);
        var detail = await client.Staff.GetAsync(target.EmployeeId);
        var action = new AccountActionRequest { UserId = target.UserId, RowVer = detail.Employee.RowVer, Reason = "Version" };
        await client.Staff.ResetPasswordAsync(action);
        await Assert.ThrowsAsync<ConcurrencyException>(() => client.Staff.LockAccountAsync(action));
        Assert.Equal(UserStatus.Active, (await client.Staff.GetAsync(target.EmployeeId)).Employee.AccountStatus);
    }

    [SqlFact]
    public async Task StaffAndAccountantCannotUseStaffService()
    {
        foreach (var position in new[] { Position.Staff, Position.Accountant })
        {
            var client = new M14TestClient();
            await client.LoginAsync(await client.CreateIdentityAsync(position));
            await Assert.ThrowsAsync<ForbiddenException>(() => client.Staff.ListAsync(new()));
            await Assert.ThrowsAsync<ForbiddenException>(() => client.Staff.CreateAsync(new()));
            Assert.NotEmpty(await client.Catalog.ListAreasAsync());
            await Assert.ThrowsAsync<ForbiddenException>(() => client.Catalog.CreateAreaAsync(new()));
            if (position == Position.Accountant) Assert.NotEmpty(await client.Catalog.ListSettingsAsync());
        }
    }

    [SqlFact]
    public async Task SettingsValidateRangePersistActorAndAuditOldNewValues()
    {
        var client = new M14TestClient();
        var manager = await client.LoginNewManagerAsync();
        var before = Assert.Single(await client.Catalog.ListSettingsAsync(), x => x.Key == "Booking.HoldMinutes");
        Assert.Equal(5m, before.Minimum);
        Assert.Equal(120m, before.Maximum);
        await Assert.ThrowsAsync<BusinessException>(() => client.Catalog.UpdateSettingAsync(new(before.Key, "121", before.Value)));
        var value = before.Value == "16" ? "17" : "16";
        try
        {
            await client.Catalog.UpdateSettingAsync(new(before.Key, value, before.Value));
            Assert.Equal(value, Assert.Single(await client.Catalog.ListSettingsAsync(), x => x.Key == before.Key).Value);
            var audit = await client.ReadAsync("SELECT TOP(1) OldValues, NewValues FROM dbo.AuditLogs WHERE UserId=@id AND ActionCode='Setting.Update' ORDER BY Id DESC;",
                r => (Old: r.GetString("OldValues"), New: r.GetString("NewValues")), manager.UserId);
            Assert.Contains("\"Value\":\"" + before.Value + "\"", audit.Old);
            Assert.Contains("\"Value\":\"" + value + "\"", audit.New);
            var actor = await client.ReadAsync("SELECT UpdatedByUserId FROM dbo.SystemSettings WHERE SettingKey='Booking.HoldMinutes' AND UpdatedByUserId=@id;", r => r.GetInt32("UpdatedByUserId"), manager.UserId);
            Assert.Equal(manager.UserId, actor);
            await Assert.ThrowsAsync<ConcurrencyException>(() => client.Catalog.UpdateSettingAsync(new(before.Key, "18", before.Value)));
        }
        finally { await client.Catalog.UpdateSettingAsync(new(before.Key, before.Value, value)); }
    }

    [SqlFact]
    public async Task NewPolicyReplacesActiveVersionAndPreservesHistory()
    {
        var client = new M14TestClient();
        await client.LoginNewManagerAsync();
        var previous = Assert.Single(await client.Catalog.ListPoliciesAsync(CancellationScope.All, false));
        await Assert.ThrowsAsync<BusinessException>(() => client.Catalog.CreatePolicyAsync(new() { Name = M14TestClient.UniqueName(), Rules = [new(3, 50)] }));
        var id = await client.Catalog.CreatePolicyAsync(new() { Name = M14TestClient.UniqueName(), Rules = [new(7, 100), new(0, 0)] });
        try
        {
            var history = await client.Catalog.ListPoliciesAsync(CancellationScope.All);
            Assert.False(Assert.Single(history, p => p.Id == previous.Id).IsActive);
            var current = Assert.Single(history, p => p.Id == id);
            Assert.True(current.IsActive);
            Assert.Equal(2, current.Rules.Count);
            await Assert.ThrowsAsync<BusinessException>(() => client.Catalog.SetPolicyActiveAsync(id, true, false));
            await Assert.ThrowsAsync<ConcurrencyException>(() => client.Catalog.SetPolicyActiveAsync(id, false, true));
        }
        finally { await client.Catalog.SetPolicyActiveAsync(previous.Id, false, true); }
    }

    [SqlFact]
    public async Task CatalogAndHolidayOperationsUseApplicationGrantedColumnsOnly()
    {
        var client = new M14TestClient();
        await client.LoginNewManagerAsync();
        var areaId = await client.Catalog.CreateAreaAsync(new() { Name = M14TestClient.UniqueName(), Province = "Hà Nội", AreaGroup = AreaGroup.InnerCity });
        var area = Assert.Single(await client.Catalog.ListAreasAsync(), x => x.Id == areaId);
        await client.Catalog.UpdateAreaAsync(area, new() { Name = area.Name + " sửa", Province = area.Province, AreaGroup = AreaGroup.Suburb });
        await Assert.ThrowsAsync<ConcurrencyException>(() => client.Catalog.UpdateAreaAsync(area, new() { Name = area.Name, Province = area.Province, AreaGroup = area.AreaGroup }));
        foreach (var kind in new[] { CatalogKind.Amenity, CatalogKind.RoomCategory })
        {
            var id = await client.Catalog.CreateItemAsync(kind, new(M14TestClient.UniqueName()));
            var item = Assert.Single(await client.Catalog.ListItemsAsync(kind), x => x.Id == id);
            await client.Catalog.UpdateItemAsync(kind, item, new(item.Name + " sửa"));
            await client.Catalog.SetActiveAsync(kind, id, true, false);
            Assert.DoesNotContain(await client.Catalog.LookupAsync(kind), x => x.Id == id);
            await Assert.ThrowsAsync<ConcurrencyException>(() => client.Catalog.SetActiveAsync(kind, id, true, false));
            await client.Catalog.SetActiveAsync(kind, id, false, true);
            Assert.Contains(await client.Catalog.LookupAsync(kind), x => x.Id == id);
            var duplicate = await Assert.ThrowsAsync<BusinessException>(() => client.Catalog.CreateItemAsync(kind, new(item.Name + " sửa")));
            Assert.Equal(kind == CatalogKind.Amenity ? "UQ_Amenities_Name" : "UQ_RoomCategories_Name", duplicate.ConstraintName);
        }
        await client.Catalog.SetActiveAsync(CatalogKind.Area, areaId, true, false);
        var date = new DateOnly(7000, 1, 1).AddDays(Random.Shared.Next(100000));
        var holiday = new HolidayItem(date, M14TestClient.UniqueName());
        await client.Catalog.CreateHolidayAsync(holiday);
        await client.Catalog.RenameHolidayAsync(new(date, holiday.Name + " sửa", holiday.Name));
        Assert.Contains(await client.Catalog.ListHolidaysAsync(date.Year), x => x.Date == date && x.Name.EndsWith(" sửa", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ConcurrencyException>(() => client.Catalog.RenameHolidayAsync(new(date, holiday.Name, holiday.Name)));
        await client.Sessions.HeartbeatAsync();
        await client.Sessions.LogoutAsync();
    }

    [SqlFact]
    public async Task UnlockScreenCountsFailuresEndsSessionAtLimitAndChecksExpiration()
    {
        var client = new M14TestClient();
        var identity = await client.LoginNewManagerAsync();
        await client.Sessions.UnlockScreenAsync(identity.Password);
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<BusinessException>(() => client.Sessions.UnlockScreenAsync("Wrong12345"));
        Assert.False(await client.Sessions.HeartbeatAsync());
        Assert.Equal("Idle", await client.ReadAsync("SELECT EndReason FROM dbo.UserSessions WHERE UserId=@id;", r => r.GetString("EndReason"), identity.UserId));
        await Assert.ThrowsAsync<SessionExpiredException>(() => client.Sessions.UnlockScreenAsync(identity.Password));
    }

    [SqlFact]
    public async Task ValidatorRejectsChangesToStampPositionEmploymentAndSessionEnd()
    {
        foreach (var sql in new[]
        {
            "UPDATE dbo.Users SET SecurityStamp=NEWID() WHERE Id=@id;",
            "UPDATE dbo.Employees SET Position='Accountant' WHERE UserId=@id;",
            "UPDATE dbo.Employees SET EmploymentStatus='Resigned' WHERE UserId=@id;",
            "UPDATE dbo.UserSessions SET EndedAt=SYSUTCDATETIME(),EndReason='AccountChanged' WHERE UserId=@id AND EndedAt IS NULL;"
        })
        {
            var client = new M14TestClient();
            var identity = await client.LoginNewManagerAsync();
            await client.ExecuteAsync(sql, identity.UserId);
            Assert.False(await client.Sessions.HeartbeatAsync());
            await Assert.ThrowsAsync<SessionExpiredException>(() => client.Catalog.CreateItemAsync(CatalogKind.Amenity, new(M14TestClient.UniqueName())));
        }
    }

    [SqlFact]
    public async Task StaffListUsesDatabasePagingFiltersAndLiteralSearch()
    {
        var client = new M14TestClient();
        await client.LoginNewManagerAsync();
        var marker = M14TestClient.UniqueName() + "%_[";
        var created = await client.Staff.CreateAsync(new() { FullName = marker, Phone = M14TestClient.Phone(), Position = Position.Staff });
        var result = await client.Staff.ListAsync(new() { Page = new PageRequest { PageSize = 10, SearchText = marker, SortColumn = "FullName", SortDescending = true }, Position = Position.Staff, AccountStatus = UserStatus.Active });
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(created.EmployeeId, Assert.Single(result.Items).EmployeeId);
        var pageTwo = await client.Staff.ListAsync(new() { Page = new PageRequest { PageNumber = 2, PageSize = 10, SearchText = marker } });
        Assert.Empty(pageTwo.Items);
        Assert.Equal(1, pageTwo.TotalCount);
        await Assert.ThrowsAsync<BusinessException>(() => client.Staff.ListAsync(new() { Page = new PageRequest { SortColumn = "FullName; DROP TABLE Users" } }));
    }

    [SqlFact]
    public async Task ConcurrentEmployeeCreationAllocatesDistinctCodesAndStoresNoPlainPassword()
    {
        var a = new M14TestClient();
        var b = new M14TestClient();
        await a.LoginNewManagerAsync();
        await b.LoginNewManagerAsync();
        var created = await Task.WhenAll(a.Staff.CreateAsync(new() { FullName = M14TestClient.UniqueName(), Phone = M14TestClient.Phone(), Position = Position.Staff }),
            b.Staff.CreateAsync(new() { FullName = M14TestClient.UniqueName(), Phone = M14TestClient.Phone(), Position = Position.Accountant }));
        Assert.NotEqual(created[0].EmployeeCode, created[1].EmployeeCode);
        foreach (var item in created)
        {
            var hash = await a.ReadAsync("SELECT PasswordHash FROM dbo.Users WHERE Id=@id;", r => r.GetString("PasswordHash"), item.UserId);
            Assert.StartsWith("PBKDF2-SHA256$600000$", hash);
            Assert.DoesNotContain(item.TemporaryPassword, hash);
            Assert.True(new PasswordHasher().Verify(item.TemporaryPassword, hash).IsValid);
        }
    }

    [SqlFact]
    public async Task InactiveAccountMessageRequiresCorrectPassword()
    {
        foreach (var update in new[]
        {
            "UPDATE dbo.Users SET Status='Disabled' WHERE Id=@id;",
            "UPDATE dbo.Employees SET EmploymentStatus='Suspended' WHERE UserId=@id;",
            "UPDATE dbo.Employees SET EmploymentStatus='Resigned' WHERE UserId=@id;"
        })
        {
            var client = new M14TestClient();
            var identity = await client.CreateIdentityAsync(Position.Staff);
            await client.ExecuteAsync(update, identity.UserId);
            Assert.Equal(LoginErrors.InvalidCredentials, (await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(identity, "Wrong12345"))).Message);
            Assert.Equal(LoginErrors.InactiveAccount, (await Assert.ThrowsAsync<BusinessException>(() => client.LoginAsync(identity))).Message);
        }
    }

    [SqlFact]
    public async Task InvalidPasswordChangesLeaveHashAndSessionUnchanged()
    {
        var client = new M14TestClient();
        var identity = await client.LoginNewManagerAsync();
        var stamp = client.Current.RequireSession().SecurityStamp;
        foreach (var request in new[]
        {
            Change("Wrong12345"),
            Change(identity.Password) with { ConfirmPassword = "Mismatch123" },
            Change(identity.Password) with { NewPassword = "short", ConfirmPassword = "short" },
            Change(identity.Password) with { NewPassword = identity.Password, ConfirmPassword = identity.Password },
            Change(identity.Password) with { NewPassword = identity.Code, ConfirmPassword = identity.Code }
        }) await Assert.ThrowsAsync<BusinessException>(() => client.Sessions.ChangePasswordAsync(request));
        Assert.Equal(stamp, client.Current.RequireSession().SecurityStamp);
        Assert.True(await client.Sessions.HeartbeatAsync());
        var hash = await client.ReadAsync("SELECT PasswordHash FROM dbo.Users WHERE Id=@id;", r => r.GetString("PasswordHash"), identity.UserId);
        Assert.True(new PasswordHasher().Verify(identity.Password, hash).IsValid);
    }

    private static ChangePasswordRequest Change(string oldPassword) => new()
        { OldPassword = oldPassword, NewPassword = "NewSecret45678", ConfirmPassword = "NewSecret45678" };
}
