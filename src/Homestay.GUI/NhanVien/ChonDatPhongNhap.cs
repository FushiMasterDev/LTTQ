using Homestay.DTO.Availability;

namespace Homestay.GUI.Staff;

// GUI navigation payload; the published DTO/service contracts remain unchanged.
/// <summary>Lựa chọn chuyển từ màn hình tìm phòng sang tạo đơn: báo giá và phòng ưu tiên.</summary>
public sealed record BookingDraftSelection(QuoteRequest Quote, int? PreferredRoomId = null);
