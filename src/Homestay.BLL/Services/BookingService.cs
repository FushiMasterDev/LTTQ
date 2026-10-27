using System.Text.RegularExpressions;
using Homestay.BLL.Jobs;
using Homestay.BLL.Security;
using Homestay.DTO.Availability;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed partial class BookingService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope, ConnectionFactory factory,
    BookingRepository repository, CustomerRepository customers, DbClock clock, SettingsProvider settings, AvailabilityService availability,
    WriteOperationRunner writes, AuditWriter audit, string machineName)
{
    // Preserve the M2.1 constructor for existing AppServices; it uses the real M1.4 validator.
    public BookingService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope, ConnectionFactory factory,
        BookingRepository repository, CustomerRepository customers, DbClock clock, SettingsProvider settings, AvailabilityService availability)
        : this(authorizer, currentSession, dataScope, factory, repository, customers, clock, settings, availability,
            new WriteOperationRunner(factory, currentSession, new WriteSessionValidator(new AuthRepository())), new AuditWriter(), Environment.MachineName) { }

    private readonly AvailabilityRepository _availabilityRepository = new();
    private readonly HoldExpiry _holdExpiry = new(new HoldExpiryRepository(), audit, machineName);

    public Task<PagedResult<BookingListItem>> ListAsync(BookingSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Code", "CustomerName", "CustomerPhone", "HomestayName", "RoomTypeName", "RoomCode", "CheckIn", "CheckOut", "Status", "PaymentState", "AssignedEmployeeName", "DeadlineAtUtc");
        StaffInputRules.Text(request.Code, 20); StaffInputRules.Text(request.CustomerName, 200);
        if (!Enum.IsDefined(request.Queue) || request.Status is { } status && !Enum.IsDefined(status) || request.CheckInFrom > request.CheckInTo || request.HomestayId is <= 0 || request.AreaId is <= 0 || request.AssignedEmployeeId is <= 0)
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
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => ReadDetailAsync(db, id, code, token), ct), cancellationToken);
    }
    internal async Task<BookingDetail> ReadDetailAsync(DbSession db, int? id, string? code, CancellationToken ct)
    {
        var scope = dataScope.BookingFilter();
        var session = currentSession.RequireSession();
        var auditAccess = authorizer.GetAccess(Permission.AuditView);
        var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
        var result = await repository.GetAsync(db, id, code, scope.Condition, scope.AddParametersTo, auditAccess == AccessLevel.Own ? session.UserId : null,
            auditAccess is AccessLevel.All or AccessLevel.Own, now, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        var detail = result.Detail;
        return detail with
        {
            Customer = Mask(detail.Customer),
            CommissionRate = session.Position is Position.Manager or Position.Accountant ? detail.CommissionRate : null,
            CanProvideArrivalDetails = detail.Booking.Status is BookingStatus.Confirmed or BookingStatus.CheckedIn or BookingStatus.Completed or BookingStatus.NoShow,
            AllowedActions = Actions(detail, result.InScope, now)
        };
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

    /// <summary>M2.3: all writes, expiry, snapshots, ledger, counter, history and audit share one validated transaction.</summary>
    public async Task<BookingDetail> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCreate);
        var normalized = BookingCreationRules.Normalize(request);
        // Validate dates/capacity using server time before opening the write transaction.
        await BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            await ValidateCreateStayAsync(db, normalized, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false);
            return true;
        }, ct), cancellationToken).ConfigureAwait(false);
        return await writes.ExecuteAsync(async (db, ct) =>
        {
            var session = currentSession.RequireSession();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            int customerId;
            if (normalized.CustomerId is { } existingId) customerId = existingId;
            else
            {
                var quick = normalized.QuickCustomer!;
                // Same resource, normalization and reuse behavior as CustomerService.CreateAsync.
                await db.AcquireAppLockAsync("CustomerPhone:" + quick.Phone, 10000, ct).ConfigureAwait(false);
                var existing = await customers.FindByPhoneAsync(db, quick.Phone, ct).ConfigureAwait(false);
                if (existing is not null) customerId = existing.Id;
                else
                {
                    customerId = await customers.CreateAsync(db, quick, session.EmployeeId, now, ct).ConfigureAwait(false);
                    await audit.WriteAsync(db, new(session.UserId, "Customer.Create", "Customers", customerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        null, new { quick.FullName, quick.Phone, quick.Email, IdNumber = StaffInputRules.MaskIdentity(quick.IdNumber), quick.Note }, null, machineName), ct).ConfigureAwait(false);
                }
            }
            // Serializes the per-customer limit across different room types and different employees.
            _ = await customers.GetAsync(db, customerId, true, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var awaiting = await repository.CountAwaitingAsync(db, customerId, now, ct).ConfigureAwait(false);
            var maxAwaiting = await settings.GetBookingMaxAwaitingPerCustomerAsync(ct, db).ConfigureAwait(false);
            if (awaiting >= maxAwaiting) throw new BusinessException($"Khách đang có {awaiting} đơn chờ thanh toán hoặc xác minh (tối đa {maxAwaiting}). Vui lòng xử lý các đơn đang chờ trước.");
            await db.AcquireAppLockAsync($"RoomType:{normalized.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            await _holdExpiry.ExpireAsync(db, normalized.RoomTypeId, now, ct).ConfigureAwait(false);
            await ValidateCreateStayAsync(db, normalized, now, ct).ConfigureAwait(false);
            var data = await _availabilityRepository.LoadAsync(db, new AvailabilitySearchRequest
                { CheckIn = normalized.CheckIn, CheckOut = normalized.CheckOut, Guests = normalized.Guests }, normalized.RoomTypeId, now, ct).ConfigureAwait(false);
            var candidate = data.Candidates.SingleOrDefault() ?? throw new BusinessException("Loại phòng không đủ điều kiện bán: kiểm tra homestay, phòng đang bật và hợp đồng phủ đến ngày trả.");
            if (normalized.RoomId is { } requestedRoom && !await repository.IsRoomActiveAsync(db, requestedRoom, normalized.RoomTypeId, ct).ConfigureAwait(false))
                throw new BusinessException("Phòng chỉ định phải đang bật và thuộc đúng loại phòng, homestay đã chọn.");
            var rate = await settings.GetBookingDepositRateAsync(ct, db).ConfigureAwait(false);
            var price = PricingCalculator.Calculate(normalized.CheckIn, normalized.CheckOut, data.Holidays, data.Prices.Select(p => p.Version).ToArray(), rate);
            if (price.DepositAmount <= 0)
                throw new BusinessException("Tiền cọc sau làm tròn bằng 0. Vui lòng báo Quản lý kiểm tra giá phòng và tỷ lệ cọc.");
            if (price.DepositAmount > price.TotalAmount)
                throw new BusinessException("Tiền cọc sau làm tròn vượt tổng tiền. Vui lòng báo Quản lý kiểm tra giá phòng và tỷ lệ cọc.");
            var policy = PricingCalculator.SelectCancellationPolicy(candidate.HomestayId, price.Nights.Any(n => n.DayType == DayType.Holiday), data.Policies);
            var room = BookingCreationRules.SelectRoom(data.Rooms.Where(r => r.IsAvailable).Select(r => new AvailableRoom(r.RoomId, r.RoomCode)), normalized.RoomId)
                ?? throw new BusinessException(BookingCreationRules.SoldOutMessage);
            now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var code = BookingCreationRules.FormatCode(now, await repository.NextSequenceAsync(db, BookingCreationRules.CounterKey(now), ct).ConfigureAwait(false));
            var hold = now.AddMinutes(await settings.GetBookingHoldMinutesAsync(ct, db).ConfigureAwait(false));
            // Header precedes ledger because FK_RoomNights_BookingId is checked immediately by SQL Server.
            BookingStateMachine.Require(null, BookingStatus.AwaitingPayment, BookingStateMachine.Actor(session.Position));
            var id = await repository.InsertAsync(db, normalized, customerId, session.EmployeeId, candidate, room.RoomId, code, now, hold, price, policy.Id, ct).ConfigureAwait(false);
            await repository.InsertNightsAsync(db, id, room.RoomId, price.Nights, ct).ConfigureAwait(false);
            await new BookingStatusWriter(repository, audit).ChangeStatusAsync(db, id, null, BookingStatus.AwaitingPayment, null,
                BookingStateMachine.Actor(session.Position), now, new(session.UserId, "Booking.Create", "Bookings", id.ToString(System.Globalization.CultureInfo.InvariantCulture), null,
                new { Code = code, CustomerId = customerId, candidate.HomestayId, normalized.RoomTypeId, RoomId = room.RoomId,
                    normalized.CheckIn, normalized.CheckOut, normalized.Guests, Status = nameof(BookingStatus.AwaitingPayment),
                    price.TotalAmount, price.DepositRate, price.DepositAmount, CommissionRate = candidate.CommissionRate, CancellationPolicyId = policy.Id, HoldExpiresAtUtc = hold }, null, machineName), ct).ConfigureAwait(false);
            var detail = await ReadDetailAsync(db, id, null, ct).ConfigureAwait(false);
            var bank = new FujiBankDetails(await settings.GetFujiBankNameAsync(ct, db).ConfigureAwait(false),
                await settings.GetFujiBankAccountNoAsync(ct, db).ConfigureAwait(false), await settings.GetFujiBankAccountNameAsync(ct, db).ConfigureAwait(false));
            return detail with { TransferInstructions = new(id, code, detail.AmountDue, bank, code, detail.HoldExpiresAtUtc) };
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ValidateCreateStayAsync(DbSession db, CreateBookingRequest request, DateTime now, CancellationToken ct)
    {
        var maxNights = await settings.GetBookingMaxNightsAsync(ct, db).ConfigureAwait(false);
        var maxGuests = await repository.GetMaxGuestsAsync(db, request.RoomTypeId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        BookingCreationRules.ValidateStay(request, now, maxNights, maxGuests);
    }
    public Task<BookingDetail> ClaimAsync(ClaimBookingRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingClaim);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        var version = request.RowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await WritableAsync(db, request.BookingId, ct).ConfigureAwait(false);
            const string claimed = "Đơn vừa được người khác nhận phụ trách. Đã tải lại.";
            if (old.AssignedEmployeeId is not null) throw new ConcurrencyException(claimed);
            CheckVersion(old, version);
            if (BookingStateMachine.IsTerminal(old.Status)) throw new BusinessException("Đơn đã kết thúc. Vui lòng tải lại đơn.");
            var session = currentSession.RequireSession();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.AssignAsync(db, old.Id, session.EmployeeId, version, true, now, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException(claimed);
            await AssignmentAuditAsync(db, "Booking.Claim", old, session.EmployeeId, ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<BookingDetail> ReassignAsync(ReassignBookingRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingAssign);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        var version = request.RowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await WritableAsync(db, request.BookingId, ct).ConfigureAwait(false);
            var session = currentSession.RequireSession();
            if (authorizer.GetAccess(Permission.BookingAssign) == AccessLevel.Own && old.AssignedEmployeeId != session.EmployeeId)
                throw new ForbiddenException("Chỉ được giao lại đơn mình đang phụ trách.");
            CheckVersion(old, version);
            if (BookingStateMachine.IsTerminal(old.Status)) throw new BusinessException("Đơn đã kết thúc. Vui lòng tải lại đơn.");
            if (old.AssignedEmployeeId == request.AssignedEmployeeId) throw new BusinessException("Vui lòng chọn người phụ trách khác người hiện tại.");
            var userId = await repository.GetAssignableUserAsync(db, request.AssignedEmployeeId, old.Id, ct).ConfigureAwait(false)
                ?? throw new BusinessException("Người nhận phải là Nhân viên hoặc Quản lý đang làm việc, có tài khoản hoạt động và phụ trách được khu vực của đơn. Vui lòng chọn lại.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.AssignAsync(db, old.Id, request.AssignedEmployeeId, version, false, now, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            await AssignmentAuditAsync(db, "Booking.Reassign", old, request.AssignedEmployeeId, ct).ConfigureAwait(false);
            await new NotificationRepository().NotifyBookingAssigneeAsync(db, userId, $"Bạn được giao phụ trách đơn {old.Code}", old.Id, now, ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<BookingDetail> CancelUnpaidAsync(CancelUnpaidBookingRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancelUnpaid);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        var version = request.RowVer.ToArray();
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
            throw new BusinessException("Lý do hủy cần từ 1 đến 500 ký tự. Vui lòng bổ sung.");
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await WritableAsync(db, request.BookingId, ct).ConfigureAwait(false);
            CheckVersion(old, version);
            if (await repository.HasActiveDepositAsync(db, old.Id, ct).ConfigureAwait(false))
                throw new BusinessException("Đơn đã có phiếu thu chờ xác minh. Vui lòng chờ kế toán xử lý trước khi hủy.");
            if (old.Status != BookingStatus.AwaitingPayment)
                throw new BusinessException("Chỉ được hủy ngay đơn Chờ thanh toán. Vui lòng tải lại đơn.");
            var session = currentSession.RequireSession();
            await new BookingStatusWriter(repository, audit).ChangeStatusAsync(db, old.Id, old.Status, BookingStatus.Cancelled, version,
                BookingStateMachine.Actor(session.Position), await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false),
                new(session.UserId, "Booking.CancelUnpaid", "Bookings", old.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new { Status = old.Status.ToString() }, new { Status = nameof(BookingStatus.Cancelled), CancelReason = reason, RefundAmount = (decimal?)null }, reason, machineName), ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    internal async Task<BookingWriteSnapshot> WritableAsync(UnitOfWork db, int id, CancellationToken ct)
    {
        await dataScope.EnsureBookingWritableAsync(db, id, ct).ConfigureAwait(false);
        var scope = dataScope.BookingFilter();
        return await repository.GetForUpdateAsync(db, id, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    }
    internal static void CheckVersion(BookingWriteSnapshot booking, byte[] rowVer)
    {
        if (!booking.RowVer.AsSpan().SequenceEqual(rowVer)) throw new ConcurrencyException();
    }
    private Task AssignmentAuditAsync(UnitOfWork db, string action, BookingWriteSnapshot old, int employeeId, CancellationToken ct) =>
        audit.WriteAsync(db, new(currentSession.RequireSession().UserId, action, "Bookings", old.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new { old.AssignedEmployeeId }, new { AssignedEmployeeId = employeeId }, null, machineName), ct);

    private IReadOnlyList<BookingAction> Actions(BookingDetail detail, bool inScope, DateTime now)
    {
        var session = currentSession.RequireSession();
        if (!inScope) return [];
        var b = detail.Booking; var result = new List<BookingAction>();
        var terminal = BookingStateMachine.IsTerminal(b.Status);
        var pending = detail.Payments.Any(p => p.Type == PaymentType.Deposit && p.Status == PaymentStatus.Pending);
        var money = detail.Payments.Any(p => p.Status == PaymentStatus.Verified && p.Type == PaymentType.Deposit);
        void Add(BookingAction action, Permission permission, bool condition) { if (condition && authorizer.Can(permission)) result.Add(action); }
        Add(BookingAction.Claim, Permission.BookingClaim, !terminal && b.AssignedEmployeeId is null);
        Add(BookingAction.Reassign, Permission.BookingAssign, !terminal && (authorizer.GetAccess(Permission.BookingAssign) != AccessLevel.Own || b.AssignedEmployeeId == session.EmployeeId));
        Add(BookingAction.RecordDeposit, Permission.PaymentRecord, b.Status == BookingStatus.AwaitingPayment && detail.HoldExpiresAtUtc > now && !pending && !money);
        Add(BookingAction.CancelUnpaid, Permission.BookingCancelUnpaid, b.Status == BookingStatus.AwaitingPayment && !pending && !money);
        var actor = BookingStateMachine.Actor(session.Position);
        var today = DateOnly.FromDateTime(now.AddHours(7));
        bool To(BookingStatus status) => BookingStateMachine.CanTransition(b.Status, status, actor, inScope);
        var openCancel = detail.ChangeRequests.Any(r => r.Status is ChangeRequestStatus.Pending or ChangeRequestStatus.AwaitingPayment);
        Add(BookingAction.ConfirmOnBehalf, Permission.BookingConfirm, To(BookingStatus.Confirmed));
        Add(BookingAction.RejectOnBehalf, Permission.BookingConfirm, To(BookingStatus.Rejected));
        Add(BookingAction.CheckIn, Permission.BookingStayManage, To(BookingStatus.CheckedIn) && today >= b.CheckIn);
        Add(BookingAction.CheckOut, Permission.BookingStayManage, To(BookingStatus.Completed));
        Add(BookingAction.MarkNoShow, Permission.BookingStayManage, To(BookingStatus.NoShow) && today > b.CheckIn);
        Add(BookingAction.CancelHomestayUnavailable, Permission.BookingChangeReview, session.Position == Position.Manager && To(BookingStatus.Cancelled) && (b.Status is BookingStatus.Deposited or BookingStatus.Confirmed));
        Add(BookingAction.SubmitCancel, Permission.BookingCancellationRequest, (b.Status is BookingStatus.Deposited or BookingStatus.Confirmed) && !openCancel);
        return result;
    }
    private CustomerItem Mask(CustomerItem item) => authorizer.Can(Permission.CustomerIdentityView) ? item : item with { IdNumber = StaffInputRules.MaskIdentity(item.IdNumber), IsIdentityMasked = true };
    private static bool IsBookingCode(string text) => Regex.IsMatch(text, "^FJ[0-9]{6}-?[0-9]{3,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && text.Length <= 20;
    private static string? TryPhone(string text) { try { return PhoneNumber.Normalize(text); } catch (BusinessException) { return null; } }
}
