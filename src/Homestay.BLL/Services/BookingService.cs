using System.Text.RegularExpressions;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed class BookingService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope, ConnectionFactory factory,
    BookingRepository repository, CustomerRepository customers, DbClock clock, SettingsProvider settings, AvailabilityService availability)
{
    public Task<PagedResult<BookingListItem>> ListAsync(BookingSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Code", "CustomerName", "CustomerPhone", "HomestayName", "RoomTypeName", "RoomCode", "CheckIn", "CheckOut", "Status", "PaymentState", "AssignedEmployeeName", "DeadlineAtUtc");
        StaffInputRules.Text(request.Code, 20); StaffInputRules.Text(request.CustomerName, 200);
        if (!Enum.IsDefined(request.Queue) || request.Status is { } status && !Enum.IsDefined(status) || request.CheckInFrom > request.CheckInTo || request.HomestayId is <= 0)
            throw new BusinessException("Bộ lọc đơn không hợp lệ. Vui lòng chọn lại.");
        var normalized = request with { Code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim(), Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : PhoneNumber.Normalize(request.Phone) };
        // Cùng một ô tìm: mã đầy đủ/SĐT đầy đủ là tra chính xác toàn hệ thống (5.1), tên hoặc từ khóa khác vẫn lọc phạm vi.
        if (normalized.Code is null && normalized.Phone is null && !string.IsNullOrWhiteSpace(request.Page.SearchText))
        {
            var text = request.Page.SearchText.Trim();
            if (IsBookingCode(text)) normalized = normalized with { Code = text, Page = request.Page with { SearchText = null } };
            else if (TryPhone(text) is { } phone) normalized = normalized with { Phone = phone, Page = request.Page with { SearchText = null } };
        }
        var scope = dataScope.BookingFilter();
        var employee = currentSession.RequireSession().EmployeeId;
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.ListAsync(db, normalized, scope.Condition, scope.AddParametersTo, employee, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false), ct), cancellationToken);
    }

    public Task<BookingDetail> GetAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        return GetCoreAsync(bookingId, null, cancellationToken);
    }
    public Task<BookingDetail> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        StaffInputRules.Text(code, 20);
        if (string.IsNullOrWhiteSpace(code)) throw new BusinessException("Vui lòng nhập mã đơn.");
        return GetCoreAsync(null, code, cancellationToken);
    }
    private Task<BookingDetail> GetCoreAsync(int? id, string? code, CancellationToken cancellationToken)
    {
        var scope = dataScope.BookingFilter();
        var session = currentSession.RequireSession();
        var auditAccess = authorizer.GetAccess(Permission.AuditView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            var result = await repository.GetAsync(db, id, code, scope.Condition, scope.AddParametersTo, auditAccess == AccessLevel.Own ? session.UserId : null,
                auditAccess is AccessLevel.All or AccessLevel.Own, now, token).ConfigureAwait(false) ?? throw new NotFoundException();
            var detail = result.Detail;
            return detail with
            {
                Customer = Mask(detail.Customer),
                CommissionRate = session.Position is Position.Manager or Position.Accountant ? detail.CommissionRate : null,
                CanProvideArrivalDetails = detail.Booking.Status is BookingStatus.Confirmed or BookingStatus.CheckedIn or BookingStatus.Completed or BookingStatus.NoShow,
                AllowedActions = Actions(detail, result.InScope, now)
            };
        }, ct), cancellationToken);
    }
    // Cùng hợp đồng báo giá cho F07; toàn bộ công thức nằm trong PricingCalculator qua AvailabilityService.
    public Task<Homestay.DTO.Availability.BookingQuote> QuoteAsync(Homestay.DTO.Availability.QuoteRequest request, CancellationToken cancellationToken = default)
        => availability.QuoteAsync(request, cancellationToken);

    public Task<EmployeeDashboard> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        var scope = dataScope.BookingFilter(); var session = currentSession.RequireSession();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.DashboardAsync(db, scope.Condition, scope.AddParametersTo, session.EmployeeId, session.UserId,
                await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false), ct), cancellationToken);
    }
    public Task<QuickSearchResult> QuickSearchAsync(string text, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        authorizer.Require(Permission.CustomerManage);
        StaffInputRules.Text(text, 200);
        var term = text?.Trim() ?? "";
        if (term.Length == 0) return Task.FromResult(new QuickSearchResult([], []));
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            IReadOnlyList<BookingListItem> bookings = [];
            if (IsBookingCode(term))
            {
                bookings = await repository.QuickSearchByCodeAsync(db, term, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false);
            }
            var phone = TryPhone(term);
            var matches = (await customers.QuickSearchAsync(db, term, phone, token).ConfigureAwait(false)).Select(Mask).ToArray();
            return new QuickSearchResult(bookings, matches);
        }, ct), cancellationToken);
    }
    public async Task<BookingTransferInstructions> GetTransferInstructionsAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        var detail = await GetAsync(bookingId, cancellationToken).ConfigureAwait(false);
        var bank = new Homestay.DTO.Availability.FujiBankDetails(await settings.GetFujiBankNameAsync(cancellationToken).ConfigureAwait(false),
            await settings.GetFujiBankAccountNoAsync(cancellationToken).ConfigureAwait(false), await settings.GetFujiBankAccountNameAsync(cancellationToken).ConfigureAwait(false));
        return new(detail.Booking.Id, detail.Booking.Code, detail.AmountDue, bank, detail.Booking.Code, detail.HoldExpiresAtUtc);
    }

    /// <summary>M2.3: Require BookingCreate, kiểm phiên; khách hoặc tạo nhanh (khóa CustomerPhone, tái dùng khách trùng);
    /// khóa Customer để kiểm MaxAwaitingPerCustomer, khóa RoomType:{id}, dùng DbClock và nhả giữ quá hạn không Pending;
    /// kiểm bán/phòng/ngày/capacity, gọi PricingCalculator, lưu snapshot giá/chính sách, Counters/RoomNights,
    /// lịch sử/audit/thông báo trong cùng transaction. Tạo mới chưa có RowVer gốc.</summary>
    public Task<BookingDetail> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("M2.3: tạo đơn chưa triển khai.");
    /// <summary>M2.4: Require BookingClaim + phạm vi; chỉ đơn chưa giao, kiểm RowVer trong transaction, audit/thông báo.</summary>
    public Task<BookingDetail> ClaimAsync(ClaimBookingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("M2.4: nhận phụ trách chưa triển khai.");
    /// <summary>M2.4: Require BookingAssign + phạm vi; NV chỉ giao đơn mình phụ trách; người nhận Active,
    /// kiểm RowVer, không đổi CreatedByEmployeeId, audit/thông báo trong transaction.</summary>
    public Task<BookingDetail> ReassignAsync(ReassignBookingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("M2.4: giao lại chưa triển khai.");
    /// <summary>M2.4: Require BookingCancelUnpaid + phạm vi, RowVer; AwaitingPayment, lý do bắt buộc,
    /// không phiếu Pending/Verified; đổi trạng thái qua máy trạng thái, nhả RoomNights và ghi lịch sử/audit/thông báo cùng transaction.</summary>
    public Task<BookingDetail> CancelUnpaidAsync(CancelUnpaidBookingRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("M2.4: hủy đơn chưa có tiền chưa triển khai.");

    private IReadOnlyList<BookingAction> Actions(BookingDetail detail, bool inScope, DateTime now)
    {
        var session = currentSession.RequireSession();
        if (!inScope || session.Position == Position.Accountant) return [];
        var b = detail.Booking; var result = new List<BookingAction>();
        var terminal = b.Status is BookingStatus.Completed or BookingStatus.Expired or BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.NoShow;
        var pending = detail.Payments.Any(p => p.Status == PaymentStatus.Pending);
        var money = detail.Payments.Any(p => p.Status == PaymentStatus.Verified && p.Type is PaymentType.Deposit or PaymentType.AdditionalDeposit);
        void Add(BookingAction action, Permission permission, bool condition) { if (condition && authorizer.Can(permission)) result.Add(action); }
        Add(BookingAction.Claim, Permission.BookingClaim, !terminal && b.AssignedEmployeeId is null);
        Add(BookingAction.Reassign, Permission.BookingAssign, !terminal && (authorizer.GetAccess(Permission.BookingAssign) != AccessLevel.Own || b.AssignedEmployeeId == session.EmployeeId));
        Add(BookingAction.RecordDeposit, Permission.PaymentRecord, b.Status == BookingStatus.AwaitingPayment && detail.HoldExpiresAtUtc >= now && !pending && !money);
        Add(BookingAction.CancelUnpaid, Permission.BookingCancelUnpaid, b.Status == BookingStatus.AwaitingPayment && !pending && !money);
        return result;
    }
    private CustomerItem Mask(CustomerItem item) => authorizer.Can(Permission.CustomerIdentityView) ? item : item with { IdNumber = StaffInputRules.MaskIdentity(item.IdNumber), IsIdentityMasked = true };
    private static bool IsBookingCode(string text) => Regex.IsMatch(text, "^FJ[0-9]{6}-?[0-9]{3,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && text.Length <= 20;
    private static string? TryPhone(string text) { try { return PhoneNumber.Normalize(text); } catch (BusinessException) { return null; } }
}
