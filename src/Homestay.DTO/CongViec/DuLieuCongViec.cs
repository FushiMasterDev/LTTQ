using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Tasks;

public sealed record WorkTaskValues
{
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public int AssigneeEmployeeId { get; init; }
    public int? BookingId { get; init; }
    public int? HomestayId { get; init; }
    public WorkTaskPriority Priority { get; init; } = WorkTaskPriority.Medium;
    // Local wall time in Vietnam. Must have DateTimeKind.Unspecified; BLL converts to UTC.
    public DateTime? DueAtVietnam { get; init; }
}
public sealed record UpdateWorkTaskRequest(int TaskId, WorkTaskValues Values, byte[] RowVer);
public sealed record WorkTaskActionRequest(int TaskId, byte[] RowVer);
public sealed record CompleteWorkTaskRequest(int TaskId, string ResultNote, byte[] RowVer);
public sealed record CancelWorkTaskRequest(int TaskId, string Reason, byte[] RowVer);
public sealed record WorkTaskSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public int? AssigneeEmployeeId { get; init; }
    public WorkTaskStatus? Status { get; init; }
    public bool OnlyOverdue { get; init; }
}
public sealed record WorkTaskItem
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }
    public int AssigneeEmployeeId { get; init; }
    public string AssigneeName { get; init; } = "";
    public int CreatedByUserId { get; init; }
    public int? BookingId { get; init; }
    public string? BookingCode { get; init; }
    public int? HomestayId { get; init; }
    public WorkTaskPriority Priority { get; init; }
    public WorkTaskStatus Status { get; init; }
    public DateTime? DueAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? ResultNote { get; init; }
    public bool IsOverdue { get; init; }
    public byte[] RowVer { get; init; } = [];
}
