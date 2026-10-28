namespace Homestay.DTO.Common;

/// <summary>Một mục tra cứu ngắn (id, tên, mã) dùng cho ô chọn/combobox.</summary>
public sealed record Lookup
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
}
