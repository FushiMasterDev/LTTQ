using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;
namespace Homestay.BLL.Rules;

public enum HomestayTransitionActor { Staff, Manager, Accountant, System }
public sealed record HomestayTransition(HomestayStatus? From, HomestayStatus To,
    IReadOnlyList<HomestayTransitionActor> Actors, bool RequiresReason = false, bool DifferentCreator = false);

// Business prerequisites (approval checklist, active contract/bookings, area) are checked by services.
public static class HomestayStateMachine
{
    private static IReadOnlyList<HomestayTransitionActor> Actors(params HomestayTransitionActor[] actors) => Array.AsReadOnly(actors);
    public static IReadOnlyList<HomestayTransition> Transitions { get; } = Array.AsReadOnly(new HomestayTransition[]
    {
        new(null,HomestayStatus.Draft,Actors(HomestayTransitionActor.Staff,HomestayTransitionActor.Manager)),
        new(HomestayStatus.Draft,HomestayStatus.PendingReview,Actors(HomestayTransitionActor.Staff,HomestayTransitionActor.Manager)),
        new(HomestayStatus.Draft,HomestayStatus.Ended,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.PendingReview,HomestayStatus.Active,Actors(HomestayTransitionActor.Manager),false,true),
        new(HomestayStatus.PendingReview,HomestayStatus.Draft,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.PendingReview,HomestayStatus.Ended,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.Active,HomestayStatus.PendingReview,Actors(HomestayTransitionActor.System)),
        new(HomestayStatus.Active,HomestayStatus.Suspended,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.Active,HomestayStatus.Ended,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.Suspended,HomestayStatus.Active,Actors(HomestayTransitionActor.Manager),true),
        new(HomestayStatus.Suspended,HomestayStatus.Ended,Actors(HomestayTransitionActor.Manager),true)
    });
    public static HomestayTransitionActor Actor(Position position) => position switch
    {
        Position.Staff => HomestayTransitionActor.Staff,
        Position.Manager => HomestayTransitionActor.Manager,
        Position.Accountant => HomestayTransitionActor.Accountant,
        _ => throw new ForbiddenException()
    };
    public static HomestayTransition? Find(HomestayStatus? from, HomestayStatus to) => Transitions.SingleOrDefault(t => t.From == from && t.To == to);
    public static bool CanTransition(HomestayStatus? from, HomestayStatus to, HomestayTransitionActor actor,
        int createdByUserId, int actingUserId, bool inScope = true) => Find(from, to) is { } rule && rule.Actors.Contains(actor)
        && (inScope || actor == HomestayTransitionActor.System) && (!rule.DifferentCreator || createdByUserId != actingUserId);
    public static HomestayTransition Require(HomestayStatus? from, HomestayStatus to, HomestayTransitionActor actor,
        int createdByUserId, int actingUserId, string? reason = null, bool inScope = true)
    {
        var rule = Find(from, to) ?? throw new BusinessException("Không thể chuyển trạng thái homestay này. Vui lòng tải lại hồ sơ.");
        if (!CanTransition(from, to, actor, createdByUserId, actingUserId, inScope)) throw new ForbiddenException();
        if (rule.RequiresReason && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500))
            throw new BusinessException("Vui lòng nhập lý do từ 1 đến 500 ký tự.");
        return rule;
    }
}
