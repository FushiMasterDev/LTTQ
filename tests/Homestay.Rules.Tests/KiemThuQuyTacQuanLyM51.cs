using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;
using Homestay.DTO.Tasks;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử quy tắc công việc và thao tác quản lý M5.1.</summary>
public sealed class M51ManagerRulesTests
{
    public static IEnumerable<object[]> TaskTransitions()
    {
        foreach (var state in Enum.GetValues<WorkTaskStatus>().Append((WorkTaskStatus)999))
        foreach (var operation in Enum.GetValues<WorkTaskOperation>().Append((WorkTaskOperation)999))
        foreach (var assignee in new[] { false, true })
        foreach (var creator in new[] { false, true })
        foreach (var manager in new[] { false, true })
        foreach (var started in new[] { false, true })
        {
            // Explicit event table: no InProgress enum/status is introduced.
            var allowed = new HashSet<WorkTaskOperation>();
            if (state == WorkTaskStatus.Open)
            {
                if (assignee) { allowed.Add(WorkTaskOperation.Complete); if (!started) allowed.Add(WorkTaskOperation.Start); }
                if (manager) { allowed.Add(WorkTaskOperation.Edit); if (creator) allowed.Add(WorkTaskOperation.Cancel); }
            }
            yield return [state, operation, assignee, creator, manager, started, allowed.Contains(operation)];
        }
    }
    [Theory, MemberData(nameof(TaskTransitions))]
    public void TaskEventTable(WorkTaskStatus state, WorkTaskOperation operation, bool assignee, bool creator, bool manager, bool started, bool allowed)
    {
        Assert.Equal(allowed, WorkTaskRules.CanApply(state, operation, assignee, creator, manager, started));
        if (!allowed) Assert.Throws<BusinessException>(() => WorkTaskRules.Next(state, operation, assignee, creator, manager, started));
        else Assert.Equal(operation switch { WorkTaskOperation.Complete => WorkTaskStatus.Done, WorkTaskOperation.Cancel => WorkTaskStatus.Cancelled, _ => WorkTaskStatus.Open },
            WorkTaskRules.Next(state, operation, assignee, creator, manager, started));
    }
    [Fact]
    public void TaskTextDueTimeAndPriorityValidation()
    {
        var values = WorkTaskRules.Normalize(new() { AssigneeEmployeeId = 1, Title = "  Việc  ", Description = "  chi tiết  ", DueAtVietnam = new(2026, 12, 1, 1, 30, 0) });
        Assert.Equal("Việc", values.Title); Assert.Equal("chi tiết", values.Description);
        Assert.Equal(new DateTime(2026, 11, 30, 18, 30, 0, DateTimeKind.Utc), WorkTaskRules.DueUtc(values.DueAtVietnam));
        Assert.Null(WorkTaskRules.DueUtc(null));
        Assert.Throws<BusinessException>(() => WorkTaskRules.DueUtc(DateTime.UtcNow));
        Assert.Throws<BusinessException>(() => WorkTaskRules.DueUtc(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local)));
        Assert.Throws<BusinessException>(() => WorkTaskRules.DueUtc(DateTime.MinValue));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { Title = "  " }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { Title = new string('a', 201) }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { Description = new string('a', 1001) }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { AssigneeEmployeeId = 0 }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { BookingId = -1 }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Normalize(values with { Priority = (WorkTaskPriority)0 }));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Result(" \t "));
        Assert.Throws<BusinessException>(() => WorkTaskRules.Result(new string('a', 501)));
        Assert.Equal("Kết quả", WorkTaskRules.Result(" Kết quả "));
        Assert.Equal(500, WorkTaskRules.Result(new string('a', 500)).Length);
    }
    private static (StaffService Staff, SessionService Sessions, TaskService Tasks, BookingService Bookings) Client(Position role)
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { UserId = 10, EmployeeId = 20, SessionId = 30, Position = role });
        var factory = new ConnectionFactory("Server=unused;Database=unused;Integrated Security=true", 1);
        var auth = new Authorizer(current); var scope = new DataScope(current); var accounts = new AuthRepository();
        var validator = new WriteSessionValidator(accounts); var writes = new WriteOperationRunner(factory, current, validator);
        var clock = new DbClock(factory); var settings = new SettingsProvider(factory); var audit = new AuditWriter(); var hasher = new PasswordHasher();
        var availability = new AvailabilityService(auth, current, factory, new AvailabilityRepository(), clock, settings);
        return (new(auth, current, factory, writes, new StaffRepository(), accounts, clock, hasher, audit, "M51"),
            new(auth, current, writes, validator, accounts, clock, settings, hasher, audit, "M51"),
            new(auth, current, scope, factory, writes, new TaskRepository(), new NotificationRepository(), clock, audit, "M51"),
            new(auth, current, scope, factory, new BookingRepository(), new CustomerRepository(), clock, settings, availability, writes, audit, "M51"));
    }
    [Fact]
    public async Task BadInputAndSelfActionsFailBeforeConnecting()
    {
        var c = Client(Position.Manager);
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(1, [1, 1], false, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(1, [0], false, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(1, [], false, [])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.ChangePositionAsync(new(20, Position.Staff, "Lý do", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.TerminateEmploymentAsync(new(20, "Lý do", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.ChangePositionAsync(new(1, (Position)999, "Lý do", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.TerminateEmploymentAsync(new(1, "Lý do", new byte[8]) { Status = EmploymentStatus.Active }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Sessions.ForceLogoutAsync(new(30, "Lý do")));
        await Assert.ThrowsAsync<BusinessException>(() => c.Sessions.ForceLogoutAsync(new(1, "  ")));
        await Assert.ThrowsAsync<BusinessException>(() => c.Tasks.CompleteAsync(new(1, "  ", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Tasks.CancelAsync(new(1, "  ", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Tasks.StartAsync(new(1, [])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Tasks.ListMineAsync(new() { Page = new() { SortColumn = "t.Id;DROP TABLE WorkTasks" } }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.AssignManyAsync(new([], 1)));
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.AssignManyAsync(new(Enumerable.Range(1, 101).Select(id => new BookingAssignmentItem(id, new byte[8])).ToArray(), 1)));
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.AssignManyAsync(new([new(1, new byte[8]), new(1, new byte[8])], 1)));
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.ListExceptionsAsync(new() { Kind = (BookingExceptionKind)999 }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Sessions.ListSessionHistoryAsync(new()));
    }
    [Theory]
    [InlineData(Position.Staff)]
    [InlineData(Position.Accountant)]
    public async Task ManagerRightsAreCheckedBeforeInput(Position role)
    {
        var c = Client(role);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Staff.SetAreasAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Staff.ChangePositionAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Staff.TerminateEmploymentAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Sessions.ForceLogoutAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Sessions.ListOpenSessionsAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Tasks.CreateAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Tasks.UpdateAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Tasks.CancelAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Bookings.AssignManyAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Bookings.ListExceptionsAsync(null!));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Bookings.GetManagerDashboardAsync());
    }
}
