using Homestay.DTO.Availability;

namespace Homestay.GUI.Staff;

// GUI navigation payload; the published DTO/service contracts remain unchanged.
public sealed record BookingDraftSelection(QuoteRequest Quote, int? PreferredRoomId = null);
