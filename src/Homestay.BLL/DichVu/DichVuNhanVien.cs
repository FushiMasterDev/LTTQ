using System.Net.Mail;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ nhân viên: danh sách, chi tiết, tạo tài khoản, khóa/mở khóa và đặt lại mật khẩu.</summary>
public sealed partial class StaffService(Authorizer authorizer, ICurrentSession currentSession, ConnectionFactory factory, WriteOperationRunner writes,
    StaffRepository repository, AuthRepository accounts, DbClock clock, PasswordHasher hasher, AuditWriter audit, string machineName)
{
    public Task<PagedResult<EmployeeListItem>> ListAsync(EmployeeSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeManage);
        ArgumentNullException.ThrowIfNull(request);
        var page = request.Page;
        if (page is null || page.PageNumber < 1 || page.PageSize is not (10 or 20 or 50 or 100) || (long)(page.PageNumber - 1) * page.PageSize > int.MaxValue)
            throw new BusinessException("Trang không hợp lệ. Vui lòng chọn cỡ trang 10, 20, 50 hoặc 100.");
        if (page.SearchText?.Length > 200) throw new BusinessException("Từ khóa tối đa 200 ký tự. Vui lòng rút gọn từ khóa.");
        if (page.SortColumn is not (null or "" or "EmployeeCode" or "FullName" or "Phone" or "Position" or "EmploymentStatus" or "AccountStatus" or "LastLoginAtUtc"))
            throw new BusinessException("Cột sắp xếp không hợp lệ. Vui lòng chọn lại cột.");
        if (request.Position is { } position && !Enum.IsDefined(position) || request.AccountStatus is { } status && !Enum.IsDefined(status))
            throw new BusinessException("Bộ lọc không hợp lệ. Vui lòng chọn lại chức vụ hoặc trạng thái.");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.ListAsync(db, request, token), ct), cancellationToken);
    }

    public Task<EmployeeDetail> GetAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeManage);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.GetAsync(db, employeeId, token).ConfigureAwait(false) ?? throw new NotFoundException(), ct), cancellationToken);
    }

    public Task<CreateEmployeeResult> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeManage);
        authorizer.Require(Permission.AccountManage);
        ArgumentNullException.ThrowIfNull(request);
        var name = request.FullName?.Trim() ?? "";
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (name.Length is < 1 or > 100) throw new BusinessException("Họ tên phải có từ 1 đến 100 ký tự. Vui lòng nhập lại.");
        if (email is not null && (email.Length > 256 || !MailAddress.TryCreate(email, out var address) || address.Address != email))
            throw new BusinessException("Email không hợp lệ. Vui lòng nhập lại hoặc để trống.");
        if (!Enum.IsDefined(request.Position)) throw new BusinessException("Chức vụ không hợp lệ. Vui lòng chọn lại.");
        var normalized = request with { FullName = name, Email = email, Phone = PhoneNumber.Normalize(request.Phone) };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var code = "NV" + (await repository.NextEmployeeNumberAsync(db, ct).ConfigureAwait(false)).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            var temporary = TemporaryPassword.Generate();
            PasswordHasher.ValidatePolicy(temporary, code);
            var ids = await repository.CreateAsync(db, code, normalized, hasher.Hash(temporary), await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Staff.Create", "Employees", ids.EmployeeId.ToString(), null,
                new { EmployeeCode = code, normalized.FullName, normalized.Phone, normalized.Email, normalized.Position }, null, machineName), ct).ConfigureAwait(false);
            return new CreateEmployeeResult(ids.EmployeeId, ids.UserId, code, temporary);
        }, cancellationToken);
    }

    public Task LockAccountAsync(AccountActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AccountManage);
        ValidateAccountRequest(request);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new BusinessException("Vui lòng nhập lý do khóa từ 1 đến 500 ký tự.");
        return SetStatusAsync(request with { Reason = reason }, true, cancellationToken);
    }

    public Task UnlockAccountAsync(AccountActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AccountManage);
        ValidateAccountRequest(request, preventSelf: false);
        return SetStatusAsync(request with { Reason = null }, false, cancellationToken);
    }

    private Task SetStatusAsync(AccountActionRequest request, bool locking, CancellationToken cancellationToken) => writes.ExecuteSerializedAsync("Managers", async (db, ct) =>
    {
        var account = await accounts.FindByUserIdAsync(db, request.UserId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        if (account.Status != (locking ? UserStatus.Active : UserStatus.Locked)) throw new ConcurrencyException();
        if (!locking && account.EmploymentStatus != EmploymentStatus.Active)
            throw new BusinessException("Nhân viên chưa ở trạng thái đang làm việc. Vui lòng cập nhật trạng thái làm việc trước khi mở khóa.");
        if (locking && account.Identity.Position == Position.Manager &&
            await repository.CountOtherActiveManagersAsync(db, request.UserId, ct).ConfigureAwait(false) < 1)
            throw new BusinessException("Phải còn ít nhất một Quản lý đang hoạt động. Vui lòng bổ sung Quản lý khác trước khi khóa.");
        var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
        var next = locking ? UserStatus.Locked : UserStatus.Active;
        if (await repository.ChangeStatusAsync(db, request, account.Status, next, Guid.NewGuid(), now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
        if (locking) await accounts.EndSessionsAsync(db, request.UserId, null, SessionEndReason.AccountChanged, now, ct).ConfigureAwait(false);
        await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, locking ? "Account.Lock" : "Account.Unlock", "Users", request.UserId.ToString(),
            new { Status = account.Status.ToString() }, new { Status = next.ToString() }, request.Reason, machineName), ct).ConfigureAwait(false);
    }, cancellationToken);

    public Task<ResetPasswordResult> ResetPasswordAsync(AccountActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AccountManage);
        ValidateAccountRequest(request);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var account = await accounts.FindByUserIdAsync(db, request.UserId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            var temporary = TemporaryPassword.Generate();
            PasswordHasher.ValidatePolicy(temporary, account.Identity.EmployeeCode);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.ResetPasswordAsync(db, request, account.Status, hasher.Hash(temporary), Guid.NewGuid(), now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await accounts.EndSessionsAsync(db, request.UserId, null, SessionEndReason.AccountChanged, now, ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Account.ResetPassword", "Users", request.UserId.ToString(), null,
                new { MustChangePassword = true }, null, machineName), ct).ConfigureAwait(false);
            return new ResetPasswordResult(temporary);
        }, cancellationToken);
    }

    private void ValidateAccountRequest(AccountActionRequest request, bool preventSelf = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (preventSelf && request.UserId == currentSession.RequireSession().UserId) throw new BusinessException("Không được khóa hoặc đặt lại mật khẩu của chính mình. Vui lòng dùng chức năng đổi mật khẩu.");
        if (request.RowVer is not { Length: 8 }) throw new BusinessException("Thiếu phiên bản tài khoản. Vui lòng tải lại danh sách rồi thao tác lại.");
    }
}
