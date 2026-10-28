using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Tasks;

/// <summary>Nội dung một công việc: tiêu đề, mô tả, người được giao, đơn/homestay liên quan, ưu tiên, hạn chót.</summary>
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
/// <summary>Yêu cầu sửa công việc.</summary>
public sealed record UpdateWorkTaskRequest(int TaskId, WorkTaskValues Values, byte[] RowVer);
/// <summary>Yêu cầu thao tác đơn giản trên công việc (ví dụ bắt đầu làm).</summary>
public sealed record WorkTaskActionRequest(int TaskId, byte[] RowVer);
/// <summary>Yêu cầu hoàn thành công việc kèm ghi chú kết quả.</summary>
public sealed record CompleteWorkTaskRequest(int TaskId, string ResultNote, byte[] RowVer);
/// <summary>Yêu cầu hủy công việc kèm lý do.</summary>
public sealed record CancelWorkTaskRequest(int TaskId, string Reason, byte[] RowVer);
/// <summary>Điều kiện tìm công việc: người được giao, trạng thái, chỉ việc quá hạn.</summary>
public sealed record WorkTaskSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public int? AssigneeEmployeeId { get; init; }
    public WorkTaskStatus? Status { get; init; }
    public bool OnlyOverdue { get; init; }
}
/// <summary>Một công việc hiển thị trên danh sách/bảng việc.</summary>
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
