using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;

namespace Homestay.BLL.Rules;

/// <summary>Các thao tác có thể làm trên một công việc: bắt đầu, sửa, hoàn thành, hủy.</summary>
public enum WorkTaskOperation { Start, Edit, Complete, Cancel }

/// <summary>Quy tắc công việc: thao tác nào được phép ở trạng thái nào, trạng thái kế tiếp, chuẩn hóa dữ liệu nhập và hạn chót.</summary>
public static class WorkTaskRules
{
    // Schema/spec 7.5 deliberately keep a started task Open. Start is an audited event.
    public static bool CanApply(WorkTaskStatus status, WorkTaskOperation operation, bool isAssignee, bool isCreator, bool isManager, bool started = false) =>
        status == WorkTaskStatus.Open && operation switch
        {
            WorkTaskOperation.Start => isAssignee && !started,
            WorkTaskOperation.Complete => isAssignee,
            WorkTaskOperation.Edit => isManager,
            WorkTaskOperation.Cancel => isManager && isCreator,
            _ => false
        };
    public static WorkTaskStatus Next(WorkTaskStatus status, WorkTaskOperation operation, bool isAssignee, bool isCreator, bool isManager, bool started = false)
    {
        if (!CanApply(status, operation, isAssignee, isCreator, isManager, started))
            throw new BusinessException("Không thể thực hiện thao tác ở trạng thái việc hiện tại. Vui lòng tải lại việc.");
        return operation switch { WorkTaskOperation.Complete => WorkTaskStatus.Done, WorkTaskOperation.Cancel => WorkTaskStatus.Cancelled, _ => WorkTaskStatus.Open };
    }
    public static WorkTaskValues Normalize(WorkTaskValues request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AssigneeEmployeeId <= 0 || request.BookingId is <= 0 || request.HomestayId is <= 0 || !Enum.IsDefined(request.Priority))
            throw new BusinessException("Người nhận, liên kết hoặc mức ưu tiên không hợp lệ. Vui lòng chọn lại.");
        _ = DueUtc(request.DueAtVietnam);
        return request with { Title = ListingWriteRules.Required(request.Title, 200, "tiêu đề"), Description = ListingWriteRules.Text(request.Description, 1000) };
    }
    public static DateTime? DueUtc(DateTime? vietnam)
    {
        if (vietnam is null) return null;
        if (vietnam.Value.Kind != DateTimeKind.Unspecified || vietnam.Value < DateTime.MinValue.AddHours(7))
            throw new BusinessException("Hạn việc phải là giờ Việt Nam, không kèm múi giờ. Vui lòng nhập lại.");
        return DateTime.SpecifyKind(vietnam.Value.AddHours(-7), DateTimeKind.Utc);
    }
    public static string Result(string? result) => ListingWriteRules.Required(result, 500, "kết quả");
}
