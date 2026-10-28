using Homestay.DTO.Enums;
namespace Homestay.DTO.Rooms;

/// <summary>Một loại phòng của homestay (hạng, tên, số khách tối đa, diện tích, đang bán hay không).</summary>
public sealed record RoomTypeItem(int Id, int HomestayId, int CategoryId, string CategoryName, string Name,
    int MaxGuests, int? AreaM2, string? Description, bool IsActive, byte[] RowVer);
/// <summary>Một phòng cụ thể thuộc loại phòng (mã phòng, đang dùng hay không).</summary>
public sealed record RoomItem(int Id, int RoomTypeId, string RoomCode, bool IsActive, string? Note, byte[] RowVer);
// One version groups the three schema rows; nullable levels expose incomplete legacy data.
/// <summary>Một phiên bản bảng giá của loại phòng: áp dụng từ ngày nào, giá ngày thường/cuối tuần/lễ.</summary>
public sealed record RoomPriceVersion(int RoomTypeId, DateOnly EffectiveFrom, decimal? WeekdayPrice,
    decimal? WeekendPrice, decimal? HolidayPrice, bool HasTakenEffect, byte[] RowVer);
/// <summary>Danh sách các phiên bản giá của loại phòng kèm ngày hiện tại của máy chủ.</summary>
public sealed record RoomPriceList(DateTime ServerNowUtc, DateOnly Today, IReadOnlyList<RoomPriceVersion> Versions);
/// <summary>Một lịch khóa phòng (từ ngày, đến ngày, lý do).</summary>
public sealed record RoomBlockItem(int Id, int RoomId, DateOnly StartDate, DateOnly EndDate, RoomBlockReason Reason,
    string? Note, int CreatedByUserId, byte[] RowVer);
/// <summary>Điều kiện tìm lịch khóa phòng của homestay trong khoảng ngày.</summary>
public sealed record RoomBlockSearchRequest(int HomestayId, DateOnly FromDate, DateOnly ToDate);
/// <summary>Dữ liệu nhập để tạo loại phòng mới.</summary>
public sealed record CreateRoomTypeRequest(int HomestayId, int CategoryId, string Name, int MaxGuests, int? AreaM2,
    string? Description, byte[] RowVer);
/// <summary>Yêu cầu sửa loại phòng.</summary>
public sealed record UpdateRoomTypeRequest(int RoomTypeId, int CategoryId, string Name, int MaxGuests, int? AreaM2,
    string? Description, byte[] RowVer);
/// <summary>Dữ liệu nhập để thêm phòng vào loại phòng.</summary>
public sealed record CreateRoomRequest(int RoomTypeId, string RoomCode, string? Note, byte[] RowVer);
/// <summary>Yêu cầu sửa mã phòng/ghi chú.</summary>
public sealed record UpdateRoomRequest(int RoomId, string RoomCode, string? Note, byte[] RowVer);
/// <summary>Yêu cầu bật/tắt bán một loại phòng.</summary>
public sealed record SetRoomTypeActiveRequest(int RoomTypeId, bool IsActive, byte[] RowVer);
/// <summary>Yêu cầu bật/tắt một phòng.</summary>
public sealed record SetRoomActiveRequest(int RoomId, bool IsActive, byte[] RowVer);
/// <summary>Yêu cầu thêm phiên bản giá mới cho loại phòng.</summary>
public sealed record AddRoomPriceRequest(int RoomTypeId, DateOnly EffectiveFrom, decimal WeekdayPrice,
    decimal WeekendPrice, decimal HolidayPrice, byte[] RowVer);
/// <summary>Yêu cầu sửa phiên bản giá chưa đến ngày áp dụng.</summary>
public sealed record UpdateRoomPriceRequest(int RoomTypeId, DateOnly EffectiveFrom, decimal WeekdayPrice,
    decimal WeekendPrice, decimal HolidayPrice, byte[] RowVer);
/// <summary>Yêu cầu khóa phòng trong khoảng ngày (bán kênh khác, bảo trì).</summary>
public sealed record BlockRoomRequest(int RoomId, DateOnly StartDate, DateOnly EndDate, RoomBlockReason Reason,
    string? Note, byte[] RowVer);
/// <summary>Yêu cầu gỡ khóa phòng kèm lý do.</summary>
public sealed record UnblockRoomRequest(int RoomBlockId, string Reason, byte[] RowVer);

/// <summary>Kết quả khóa/gỡ khóa phòng (id lịch khóa và RowVer mới).</summary>
public sealed record RoomBlockChangeResult(int HomestayId, int RoomBlockId, byte[] RowVer);
