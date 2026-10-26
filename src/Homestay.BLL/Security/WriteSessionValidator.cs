using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Security;

public sealed class WriteSessionValidator(AuthRepository repository) : IWriteSessionValidator
{
    public async Task ValidateAsync(SessionInfo session, UnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        if (!await IsValidAsync(session, unitOfWork, cancellationToken).ConfigureAwait(false)) throw new SessionExpiredException();
    }

    internal async Task<bool> IsValidAsync(SessionInfo session, UnitOfWork db, CancellationToken ct)
    {
        var account = await repository.FindByUserIdAsync(db, session.UserId, ct).ConfigureAwait(false);
        return account is not null && account.Status == UserStatus.Active && account.EmploymentStatus == EmploymentStatus.Active &&
            account.Identity.EmployeeId == session.EmployeeId && account.Identity.Position == session.Position &&
            account.Identity.SecurityStamp == session.SecurityStamp &&
            await repository.IsSessionOpenAsync(db, session.SessionId, session.UserId, ct).ConfigureAwait(false);
    }
}
