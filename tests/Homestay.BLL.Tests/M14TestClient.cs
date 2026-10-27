using System.Security.Cryptography;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

internal sealed record TestIdentity(int UserId, int EmployeeId, string Code, string Password);

internal sealed class M14TestClient
{
    public ConnectionFactory Factory { get; } = SqlFixture.Factory();
    public CurrentSession Current { get; } = new();
    public AuthService Auth { get; }
    public SessionService Sessions { get; }
    public StaffService Staff { get; }
    public CatalogService Catalog { get; }
    public string Machine { get; } = "M14-" + Guid.NewGuid().ToString("N");
    public static string UniqueName() => "M14 " + Guid.NewGuid().ToString("N");
    public static string Phone() => "09" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public M14TestClient()
    {
        var accountRepository = new AuthRepository();
        var validator = new WriteSessionValidator(accountRepository);
        var writes = new WriteOperationRunner(Factory, Current, validator);
        var authorizer = new Authorizer(Current);
        var clock = new DbClock(Factory);
        var settings = new SettingsProvider(Factory);
        var audit = new AuditWriter();
        var hasher = new PasswordHasher();
        Auth = new(Factory, accountRepository, clock, settings, hasher, audit, Current);
        Sessions = new(authorizer, Current, writes, validator, accountRepository, clock, settings, hasher, audit, Machine);
        Staff = new(authorizer, Current, Factory, writes, new StaffRepository(), accountRepository, clock, hasher, audit, Machine);
        Catalog = new(authorizer, Current, Factory, writes, new CatalogRepository(), clock, settings, audit, Machine);
    }

    public Task<SessionInfo> LoginAsync(TestIdentity identity, string? password = null) => Auth.LoginAsync(new()
        { Username = identity.Code, Password = password ?? identity.Password, MachineName = Machine, AppVersion = "M1.4-tests" });

    public async Task<TestIdentity> CreateIdentityAsync(Position position = Position.Manager, string? hash = null, string password = "TestSecret123")
    {
        var code = "T" + Guid.NewGuid().ToString("N")[..19];
        var encoded = hash ?? new PasswordHasher().Hash(password);
        var user = await UnitOfWork.BeginAsync(Factory, CancellationToken.None);
        await using var scope = user.ConfigureAwait(false);
        using var command = user.CreateCommand("""
            INSERT dbo.Users (Username, PasswordHash, Status, MustChangePassword) VALUES (@code, @hash, 'Active', 0);
            DECLARE @user int=CONVERT(int, SCOPE_IDENTITY());
            INSERT dbo.Employees (UserId, EmployeeCode, FullName, Phone, Position, EmploymentStatus, HasAllAreas)
            VALUES (@user, @code, @name, @phone, @position, 'Active', 1);
            SELECT @user AS UserId, CONVERT(int, SCOPE_IDENTITY()) AS EmployeeId;
            """);
        command.Parameters.AddVarChar("@code", 20, code);
        command.Parameters.AddVarChar("@hash", 255, encoded);
        command.Parameters.AddNVarChar("@name", 100, UniqueName());
        command.Parameters.AddVarChar("@phone", 16, Phone());
        command.Parameters.AddEnum("@position", 20, (Position?)position);
        var ids = Assert.Single(await user.QueryAsync(command, r => (UserId: r.GetInt32("UserId"), EmployeeId: r.GetInt32("EmployeeId")), CancellationToken.None));
        await user.CommitAsync(CancellationToken.None);
        return new(ids.UserId, ids.EmployeeId, code, password);
    }

    public async Task<TestIdentity> LoginNewManagerAsync()
    {
        var manager = await CreateIdentityAsync();
        await LoginAsync(manager);
        return manager;
    }

    public Task<T> ReadAsync<T>(string sql, Func<Microsoft.Data.SqlClient.SqlDataReader, T> map, int id) =>
        Factory.ReadAsync(async (db, ct) =>
        {
            using var command = db.CreateCommand(sql);
            command.Parameters.AddInt32("@id", id);
            return Assert.Single(await db.QueryAsync(command, map, ct));
        }, CancellationToken.None);

    public async Task ExecuteAsync(string sql, int id)
    {
        var db = await UnitOfWork.BeginAsync(Factory, CancellationToken.None);
        await using var scope = db.ConfigureAwait(false);
        using var command = db.CreateCommand(sql);
        command.Parameters.AddInt32("@id", id);
        await db.ExecuteAsync(command, CancellationToken.None);
        await db.CommitAsync(CancellationToken.None);
    }
}
