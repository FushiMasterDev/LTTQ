using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.GUI.Workflow;
using Homestay.GUI.Configuration;
using Homestay.GUI.Infrastructure;
using Microsoft.Data.SqlClient;
using Homestay.BLL.Jobs;

namespace Homestay.GUI;

// Only this composition root references DAL types, to assemble BLL as documented in M1.4.
// Forms call BLL services; no SQL, SqlConnection, commands or repository calls in GUI.
public sealed class AppServices : IAccountGateway
{
    private sealed record Runtime(CurrentSession Current, Authorizer Authorizer, AuthService Auth,
        SessionService Sessions, StaffService Staff, CatalogService Catalog, SettingsProvider Settings,
        AvailabilityService Availability, CustomerService Customers, BookingService Bookings,
        PaymentService Payments, NotificationService Notifications, FileService Files, JobRunner Jobs, DbClock Clock);
    private Runtime? runtime;
    private Runtime Ready => runtime ?? throw new InvalidOperationException("Chưa khởi tạo kết nối ứng dụng.");
    public AppSettings Settings { get; }
    public ConnectionStore Connections { get; }
    public FileLogger Logger { get; }
    public string MachineName { get; } = Environment.MachineName;
    public string AppVersion { get; } = "0.2.0";
    public int IdleMinutes { get; private set; } = 30;
    public bool IsInitialized => runtime is not null;
    public SessionInfo? Session => runtime?.Current.Session;
    public StaffService Staff => Ready.Staff;
    public CatalogService Catalog => Ready.Catalog;
    public AvailabilityService Availability => Ready.Availability;
    public CustomerService Customers => Ready.Customers;
    public BookingService Bookings => Ready.Bookings;
    public PaymentService Payments => Ready.Payments;
    public NotificationService Notifications => Ready.Notifications;
    public FileService Files => Ready.Files;
    public JobRunner Jobs => Ready.Jobs;
    public Task<DateTime> ServerNowAsync(CancellationToken token=default)=>Ready.Clock.GetUtcNowAsync(token);
    public event Action<string, object?>? NavigationRequested;
    public void Navigate(string screen, object? argument = null) => NavigationRequested?.Invoke(screen, argument);

    // Both F04 and startup use the same unauthenticated, read-only BLL contract.
    public Func<ConnectionProfile, CancellationToken, Task<int[]>> ReadSchemaVersionsAsync { get; init; }
    private static readonly int[] SupportedVersions = [1, 2, 3, 4, 10, 20];

    public AppServices(AppSettings settings, string connectionPath)
    {
        Settings = settings;
        ReadSchemaVersionsAsync = async (profile, token) =>
            (await new StartupService(CreateFactory(profile)).GetSchemaVersionsAsync(token)).ToArray();
        Connections = new ConnectionStore(connectionPath);
        Logger = new FileLogger(settings.Logging.Folder, settings.Logging.RetainDays);
    }
    public async Task CheckConnectionAsync(ConnectionProfile profile, CancellationToken token)
    {
        var versions = await ReadSchemaVersionsAsync(profile, token);
        if (!versions.Order().SequenceEqual(SupportedVersions))
            throw new InvalidDataException("Phiên bản cơ sở dữ liệu không phù hợp. Vui lòng cập nhật ứng dụng hoặc bộ cài dữ liệu.");
    }
    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (Session is not null) throw new InvalidOperationException("Không đổi kết nối khi đang có phiên đăng nhập.");
        var profile = Connections.Load();
        try
        {
            await CheckConnectionAsync(profile, token);
            var next = CreateRuntime(profile);
            var idle = await next.Settings.GetSessionIdleMinutesAsync(token);
            runtime = next;
            IdleMinutes = idle;
        }
        finally { profile.Password = ""; }
    }
    // A new login gets a new BLL session container, even after an unreachable server prevented logout.
    internal void ResetForSignIn() => runtime = null;
    private ConnectionFactory CreateFactory(ConnectionProfile profile)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = profile.Server, InitialCatalog = profile.Database,
            UserID = profile.Username, Password = profile.Password, Encrypt = profile.Encrypt,
            TrustServerCertificate = profile.TrustServerCertificate, Pooling = true
        };
        return new ConnectionFactory(builder.ConnectionString, Settings.Database.CommandTimeoutSeconds);
    }
    private Runtime CreateRuntime(ConnectionProfile profile)
    {
        var factory = CreateFactory(profile);
        var current = new CurrentSession();
        var authorizer = new Authorizer(current);
        var accounts = new AuthRepository();
        var validator = new WriteSessionValidator(accounts);
        var writes = new WriteOperationRunner(factory, current, validator);
        var clock = new DbClock(factory);
        var settings = new SettingsProvider(factory);
        var hasher = new PasswordHasher();
        var audit = new AuditWriter();
        var scope = new DataScope(current);
        var customers = new CustomerRepository();
        var bookingRepository = new BookingRepository();
        var notifications = new NotificationRepository();
        var evidence = new EvidenceRepository();
        var availability = new AvailabilityService(authorizer,current,factory,new AvailabilityRepository(),clock,settings);
        var bookings = new BookingService(authorizer,current,scope,factory,bookingRepository,customers,clock,settings,availability,writes,audit,MachineName);
        var expiryRepository = new HoldExpiryRepository();
        return new Runtime(current, authorizer,
            new AuthService(factory, accounts, clock, settings, hasher, audit, current),
            new SessionService(authorizer, current, writes, validator, accounts, clock, settings, hasher, audit, MachineName),
            new StaffService(authorizer, current, factory, writes, new StaffRepository(), accounts, clock, hasher, audit, MachineName),
            new CatalogService(authorizer, current, factory, writes, new CatalogRepository(), clock, settings, audit, MachineName),
            settings, availability,
            new CustomerService(authorizer,current,factory,writes,customers,clock,audit,MachineName), bookings,
            new PaymentService(authorizer,current,scope,writes,bookingRepository,new PaymentRepository(),evidence,notifications,clock,bookings,audit,MachineName),
            new NotificationService(current,factory,writes,notifications,clock),
            new FileService(authorizer,factory,evidence),
            new JobRunner(factory,clock,expiryRepository,new HoldExpiry(expiryRepository,audit,MachineName)),clock);
    }
    public bool Can(Permission permission) => Session is not null && Ready.Authorizer.Can(permission);
    public Task<SessionInfo> LoginAsync(LoginRequest request, CancellationToken token = default) => Ready.Auth.LoginAsync(request, token);
    public Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken token = default) => Ready.Sessions.ChangePasswordAsync(request, token);
    public Task<bool> HeartbeatAsync(CancellationToken token = default) => Ready.Sessions.HeartbeatAsync(token);
    public Task UnlockAsync(string password, CancellationToken token = default) => Ready.Sessions.UnlockScreenAsync(password, token);
    public Task LogoutAsync(CancellationToken token = default) => runtime is null ? Task.CompletedTask : Ready.Sessions.LogoutAsync(token);
}
