namespace Homestay.DTO.Common;

/// <summary>Yêu cầu phân trang: trang số mấy, cỡ trang, từ khóa tìm và cột sắp xếp.</summary>
public sealed record PageRequest
{
    public const int PageSize10 = 10;
    public const int PageSize20 = 20;
    public const int PageSize50 = 50;
    public const int PageSize100 = 100;
    public const int DefaultPageSize = PageSize50;

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;
    public string? SearchText { get; init; }

    // Tên cột logic; BLL đối chiếu danh sách cho phép trước khi sắp xếp.
    public string? SortColumn { get; init; }

    public bool SortDescending { get; init; }
}
