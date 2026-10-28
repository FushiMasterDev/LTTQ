namespace Homestay.DTO.Enums;

/// <summary>Loại yêu cầu thay đổi đơn: đổi ngày, đổi loại phòng, đổi cả hai, hủy.</summary>
public enum ChangeRequestType
{
    ChangeDates,
    ChangeRoomType,
    ChangeDatesAndRoomType,
    Cancel,
}
