using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Rules;

public enum BookingTransitionActor { Staff, Manager, Accountant, System, Internal }

public sealed record BookingTransition(BookingStatus? From, BookingStatus To,
    IReadOnlyList<BookingTransitionActor> Actors, bool OnBehalfOfOwner = false);

/// <summary>Complete spec 7.2 table. Business prerequisites (money, deadlines, scope, etc.) remain in services.</summary>
public static class BookingStateMachine
{
    private static readonly BookingTransitionActor[] Operators = [BookingTransitionActor.Staff, BookingTransitionActor.Manager];
    public static IReadOnlyList<BookingTransition> Transitions { get; } = Array.AsReadOnly(new BookingTransition[]
    {
        new(null, BookingStatus.AwaitingPayment, Array.AsReadOnly(Operators)),
        new(BookingStatus.AwaitingPayment, BookingStatus.PaymentSubmitted, Array.AsReadOnly(new[] { BookingTransitionActor.Staff, BookingTransitionActor.Manager, BookingTransitionActor.Accountant })),
        new(BookingStatus.AwaitingPayment, BookingStatus.Deposited, Array.AsReadOnly(new[] { BookingTransitionActor.Accountant })),
        new(BookingStatus.AwaitingPayment, BookingStatus.Expired, Array.AsReadOnly(new[] { BookingTransitionActor.System })),
        new(BookingStatus.AwaitingPayment, BookingStatus.Cancelled, Array.AsReadOnly(Operators)),
        new(BookingStatus.PaymentSubmitted, BookingStatus.Deposited, Array.AsReadOnly(new[] { BookingTransitionActor.Accountant })),
        new(BookingStatus.PaymentSubmitted, BookingStatus.AwaitingPayment, Array.AsReadOnly(new[] { BookingTransitionActor.Accountant })),
        new(BookingStatus.PaymentSubmitted, BookingStatus.Expired, Array.AsReadOnly(new[] { BookingTransitionActor.Accountant })),
        new(BookingStatus.Deposited, BookingStatus.Confirmed, Array.AsReadOnly(Operators), true),
        new(BookingStatus.Deposited, BookingStatus.Rejected, Array.AsReadOnly(Operators), true),
        new(BookingStatus.Deposited, BookingStatus.Cancelled, Array.AsReadOnly(new[] { BookingTransitionActor.Manager })),
        new(BookingStatus.Confirmed, BookingStatus.CheckedIn, Array.AsReadOnly(Operators), true),
        new(BookingStatus.Confirmed, BookingStatus.NoShow, Array.AsReadOnly(Operators), true),
        new(BookingStatus.Confirmed, BookingStatus.Cancelled, Array.AsReadOnly(new[] { BookingTransitionActor.Manager })),
        new(BookingStatus.Confirmed, BookingStatus.Deposited, Array.AsReadOnly(new[] { BookingTransitionActor.Internal })),
        new(BookingStatus.CheckedIn, BookingStatus.Completed, Array.AsReadOnly(Operators), true)
    });

    public static BookingTransitionActor Actor(Position position) => position switch
    {
        Position.Staff => BookingTransitionActor.Staff,
        Position.Manager => BookingTransitionActor.Manager,
        Position.Accountant => BookingTransitionActor.Accountant,
        _ => throw new ForbiddenException()
    };

    public static bool IsTerminal(BookingStatus status) => status is BookingStatus.Expired or BookingStatus.Rejected
        or BookingStatus.Cancelled or BookingStatus.NoShow or BookingStatus.Completed;

    public static BookingTransition? Find(BookingStatus? from, BookingStatus to) => Transitions.SingleOrDefault(t => t.From == from && t.To == to);

    public static bool CanTransition(BookingStatus? from, BookingStatus to, BookingTransitionActor actor, bool inScope = true) =>
        Find(from, to) is { } rule && rule.Actors.Contains(actor)
        && (inScope || actor is BookingTransitionActor.Accountant or BookingTransitionActor.System or BookingTransitionActor.Internal);

    public static BookingTransition Require(BookingStatus? from, BookingStatus to, BookingTransitionActor actor, string? note = null, bool inScope = true)
    {
        var rule = Find(from, to) ?? throw new BusinessException("Không thể chuyển trạng thái đơn này. Vui lòng tải lại đơn.");
        if (!CanTransition(from, to, actor, inScope)) throw new ForbiddenException();
        if (rule.OnBehalfOfOwner && (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 500))
            throw new BusinessException("Thao tác thay chủ cần ghi chú từ 1 đến 500 ký tự. Vui lòng bổ sung.");
        return rule;
    }
}
