namespace Homestay.DTO.Common;

/// <summary>Kết quả một trang dữ liệu: các dòng, tổng số dòng, số trang và cỡ trang.</summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = PageRequest.DefaultPageSize;
}
