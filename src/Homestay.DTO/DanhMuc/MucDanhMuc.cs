using Homestay.DTO.Enums;

namespace Homestay.DTO.Catalog;

public enum CatalogKind { Area, Amenity, RoomCategory }

public sealed record AreaItem(int Id, string Name, string Province, AreaGroup AreaGroup, bool IsActive);
public sealed record CatalogItem(int Id, string Name, bool IsActive);
public sealed record AreaRequest
{
    public string Name { get; init; } = "";
    public string Province { get; init; } = "";
    public AreaGroup AreaGroup { get; init; }
}
public sealed record CatalogNameRequest(string Name);
public sealed record HolidayItem(DateOnly Date, string Name);
public sealed record RenameHolidayRequest(DateOnly Date, string Name, string OriginalName);
public sealed record CancellationRuleItem(int MinDaysBefore, int RefundPercent);
public sealed record CancellationPolicyItem
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public CancellationScope Scope { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public IReadOnlyList<CancellationRuleItem> Rules { get; init; } = [];
}
public sealed record CreateCancellationPolicyRequest
{
    public string Name { get; init; } = "";
    public CancellationScope Scope { get; init; }
    public bool Activate { get; init; } = true;
    public IReadOnlyList<CancellationRuleItem> Rules { get; init; } = [];
}
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
public sealed record UpdateSettingRequest(string Key, string Value, string OriginalValue);
