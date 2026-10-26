using Homestay.DTO.Enums;

namespace Homestay.GUI.Common;

public sealed record Choice(object? Value, string Label)
{
    public override string ToString() => Label;
}
public static class UiNames
{
    public static string Position(Position position) => position switch
    {
        Homestay.DTO.Enums.Position.Staff => "Nhân viên",
        Homestay.DTO.Enums.Position.Manager => "Quản lý",
        Homestay.DTO.Enums.Position.Accountant => "Kế toán",
        _ => "Không xác định"
    };
    public static string UserStatus(UserStatus status) => status switch
    {
        Homestay.DTO.Enums.UserStatus.Active => "Đang hoạt động",
        Homestay.DTO.Enums.UserStatus.Locked => "Đã khóa",
        Homestay.DTO.Enums.UserStatus.Disabled => "Ngừng hoạt động",
        _ => "Không xác định"
    };
    public static string Employment(EmploymentStatus status) => status switch
    {
        EmploymentStatus.Active => "Đang làm việc",
        EmploymentStatus.Suspended => "Tạm ngừng",
        EmploymentStatus.Resigned => "Đã nghỉ việc",
        _ => status.ToString()
    };
    public static Choice[] Positions() => Enum.GetValues<Homestay.DTO.Enums.Position>()
        .Select(p => new Choice(p, Position(p))).ToArray();
    public static Choice[] AreaGroups() => [
        new(AreaGroup.InnerCity,"Nội thành"),new(AreaGroup.Suburb,"Ngoại thành"),new(AreaGroup.OtherProvince,"Tỉnh khác")];
    public static string Active(bool active) => active ? "Đang sử dụng" : "Ngừng sử dụng";
}
