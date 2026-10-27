using Homestay.DTO.Enums;
namespace Homestay.DTO.Rooms;

public sealed record RoomTypeItem(int Id, int HomestayId, int CategoryId, string CategoryName, string Name,
    int MaxGuests, int? AreaM2, string? Description, bool IsActive, byte[] RowVer);
public sealed record RoomItem(int Id, int RoomTypeId, string RoomCode, bool IsActive, string? Note, byte[] RowVer);
// One version groups the three schema rows; nullable levels expose incomplete legacy data.
public sealed record RoomPriceVersion(int RoomTypeId, DateOnly EffectiveFrom, decimal? WeekdayPrice,
    decimal? WeekendPrice, decimal? HolidayPrice, bool HasTakenEffect, byte[] RowVer);
public sealed record RoomPriceList(DateTime ServerNowUtc, DateOnly Today, IReadOnlyList<RoomPriceVersion> Versions);
public sealed record RoomBlockItem(int Id, int RoomId, DateOnly StartDate, DateOnly EndDate, RoomBlockReason Reason,
    string? Note, int CreatedByUserId, byte[] RowVer);
public sealed record RoomBlockSearchRequest(int HomestayId, DateOnly FromDate, DateOnly ToDate);
public sealed record CreateRoomTypeRequest(int HomestayId, int CategoryId, string Name, int MaxGuests, int? AreaM2,
    string? Description, byte[] RowVer);
public sealed record UpdateRoomTypeRequest(int RoomTypeId, int CategoryId, string Name, int MaxGuests, int? AreaM2,
    string? Description, byte[] RowVer);
public sealed record CreateRoomRequest(int RoomTypeId, string RoomCode, string? Note, byte[] RowVer);
public sealed record UpdateRoomRequest(int RoomId, string RoomCode, string? Note, byte[] RowVer);
public sealed record SetRoomTypeActiveRequest(int RoomTypeId, bool IsActive, byte[] RowVer);
public sealed record SetRoomActiveRequest(int RoomId, bool IsActive, byte[] RowVer);
public sealed record AddRoomPriceRequest(int RoomTypeId, DateOnly EffectiveFrom, decimal WeekdayPrice,
    decimal WeekendPrice, decimal HolidayPrice, byte[] RowVer);
public sealed record UpdateRoomPriceRequest(int RoomTypeId, DateOnly EffectiveFrom, decimal WeekdayPrice,
    decimal WeekendPrice, decimal HolidayPrice, byte[] RowVer);
public sealed record BlockRoomRequest(int RoomId, DateOnly StartDate, DateOnly EndDate, RoomBlockReason Reason,
    string? Note, byte[] RowVer);
public sealed record UnblockRoomRequest(int RoomBlockId, string Reason, byte[] RowVer);

public sealed record RoomBlockChangeResult(int HomestayId, int RoomBlockId, byte[] RowVer);
