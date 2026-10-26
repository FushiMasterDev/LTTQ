namespace Homestay.DTO.Common;

public sealed record Lookup
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
}
