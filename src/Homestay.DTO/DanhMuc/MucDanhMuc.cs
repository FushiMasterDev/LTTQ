using Homestay.DTO.Enums;

namespace Homestay.DTO.Catalog;

/// <summary>Loại danh mục đơn giản: khu vực, tiện nghi, hạng phòng.</summary>
public enum CatalogKind { Area, Amenity, RoomCategory }

/// <summary>Một khu vực (tên, tỉnh, nhóm khu vực, đang dùng hay không).</summary>
public sealed record AreaItem(int Id, string Name, string Province, AreaGroup AreaGroup, bool IsActive);
/// <summary>Một mục danh mục đơn giản (tiện nghi, hạng phòng).</summary>
public sealed record CatalogItem(int Id, string Name, bool IsActive);
/// <summary>Dữ liệu nhập để tạo/sửa khu vực.</summary>
public sealed record AreaRequest
{
    public string Name { get; init; } = "";
    public string Province { get; init; } = "";
    public AreaGroup AreaGroup { get; init; }
}
/// <summary>Dữ liệu nhập để tạo/sửa tên một mục danh mục.</summary>
public sealed record CatalogNameRequest(string Name);
/// <summary>Một ngày lễ (ngày và tên) dùng để tính giá ngày lễ.</summary>
public sealed record HolidayItem(DateOnly Date, string Name);
/// <summary>Yêu cầu đổi tên ngày lễ (kèm tên cũ để phát hiện người khác đã sửa).</summary>
public sealed record RenameHolidayRequest(DateOnly Date, string Name, string OriginalName);
/// <summary>Một mốc của chính sách hủy: hủy trước ít nhất bao nhiêu ngày thì hoàn bao nhiêu phần trăm.</summary>
public sealed record CancellationRuleItem(int MinDaysBefore, int RefundPercent);
/// <summary>Một chính sách hủy cùng các mốc hoàn tiền của nó.</summary>
public sealed record CancellationPolicyItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CancellationScope Scope { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<CancellationRuleItem> Rules { get; init; } = [];
}
/// <summary>Dữ liệu nhập để tạo chính sách hủy mới.</summary>
public sealed record CreateCancellationPolicyRequest
{
    public string Name { get; init; } = "";
    public CancellationScope Scope { get; init; }
    public bool Activate { get; init; } = true;
    public IReadOnlyList<CancellationRuleItem> Rules { get; init; } = [];
}
/// <summary>Một tham số cấu hình nghiệp vụ: khóa, giá trị, mô tả, kiểu và khoảng cho phép.</summary>
public sealed record SettingItem
{
    public string Key { get; init; } = "";
    public string Value { get; init; } = "";
    public string? Description { get; init; }
    public string ValueType { get; init; } = "";
    public decimal? Minimum { get; init; }
    public decimal? Maximum { get; init; }
    public string? Format { get; init; }
}
/// <summary>Yêu cầu đổi giá trị tham số cấu hình (kèm giá trị cũ để chống ghi đè).</summary>
public sealed record UpdateSettingRequest(string Key, string Value, string OriginalValue);
