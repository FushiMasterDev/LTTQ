using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;

namespace Homestay.BLL.Tests;

/// <summary>Bộ khởi tạo các dịch vụ quản lý (nhân viên, phiên, đơn, công việc) cho kiểm thử.</summary>
internal sealed class M51TestClient
{
    public M21TestClient Base { get; } = new();
    public StaffService Staff => Base.Login.Staff;
    public SessionService Sessions => Base.Login.Sessions;
    public BookingService Bookings => Base.Bookings;
    public TaskService Tasks { get; }
    public M51TestClient()
    {
        var current = Base.Current;
        Tasks = new(new Authorizer(current), current, new DataScope(current), Base.Factory,
            new WriteOperationRunner(Base.Factory, current, new WriteSessionValidator(new AuthRepository())), new TaskRepository(),
            new NotificationRepository(), new DbClock(Base.Factory), new AuditWriter(), Base.Login.Machine);
    }
    public Task<TestIdentity> LoginAsync(Position position = Position.Manager, int? areaId = null, bool all = true) => Base.LoginNewAsync(position, areaId, all);
    public async Task SetAreasAsync(int employeeId, int[] areas, bool all = false)
    {
        var old = await Staff.GetAreasAsync(employeeId);
        await Staff.SetAreasAsync(new(employeeId, areas, all, old.RowVer));
    }
    public async Task<DateTime> NowAsync() => await new DbClock(Base.Factory).GetUtcNowAsync(CancellationToken.None);
    public Task<int> CountAsync(string sql, int id) => Base.ScalarAsync<int>(sql, c => c.Parameters.AddInt32("@id", id));
    public static WorkTaskValues Values(int employeeId, int? booking = null, int? home = null) => new()
    {
        Title = "Việc M51 " + Guid.NewGuid().ToString("N"), Description = "Nội dung", AssigneeEmployeeId = employeeId,
        BookingId = booking, HomestayId = home, Priority = WorkTaskPriority.High
    };
}
