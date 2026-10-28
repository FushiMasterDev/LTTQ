using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử M4.2B: phân quyền và dữ liệu nhập phòng/hợp đồng/tài khoản hoàn bị chặn sớm.</summary>
public sealed class M42BInputContractTests
{
    private static (RoomService Rooms, ContractService Contracts, PaymentService Payments) Client(Position role)
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { Position = role });
        var factory = new ConnectionFactory("Server=invalid.example;Database=unused;Integrated Security=true;Connect Timeout=1", 1);
        var auth = new Authorizer(current); var scope = new DataScope(current); var clock = new DbClock(factory);
        var writes = new WriteOperationRunner(factory, current, new WriteSessionValidator(new AuthRepository()));
        return (new(auth, scope, factory, new RoomRepository(), clock, null!), new(auth, scope, factory, new ContractRepository()),
            new(auth, current, scope, writes, new BookingRepository(), new PaymentRepository(), new EvidenceRepository(), new NotificationRepository(), clock, null!, new AuditWriter(), "M42B-input"));
    }
    [Fact]
    public async Task AccountantCannotWriteRoomsOrContractsEvenWithNullArguments()
    {
        var c = Client(Position.Accountant);
        Func<Task>[] calls = [() => c.Rooms.CreateTypeAsync(null!), () => c.Rooms.UpdateTypeAsync(null!), () => c.Rooms.SetTypeActiveAsync(null!),
            () => c.Rooms.CreateRoomAsync(null!), () => c.Rooms.UpdateRoomAsync(null!), () => c.Rooms.SetRoomActiveAsync(null!),
            () => c.Rooms.AddPriceAsync(null!), () => c.Rooms.UpdatePriceAsync(null!), () => c.Rooms.BlockAsync(null!), () => c.Rooms.UnblockAsync(null!),
            () => c.Contracts.CreateAsync(null!), () => c.Contracts.ActivateAsync(null!), () => c.Contracts.ExtendAsync(null!), () => c.Contracts.TerminateAsync(null!)];
        foreach (var call in calls) await Assert.ThrowsAsync<ForbiddenException>(call);
    }
    [Theory]
    [InlineData(Position.Staff)]
    [InlineData(Position.Manager)]
    public async Task RefundAccountOnlyAccountantBeforeAnyInputOrDatabase(Position role) =>
        await Assert.ThrowsAsync<ForbiddenException>(() => Client(role).Payments.UpdateRefundAccountAsync(null!));
    [Fact]
    public async Task StaffCannotBlockOrUnblockOrWriteContractsDirectly()
    {
        var c = Client(Position.Staff);
        Func<Task>[] calls = [() => c.Rooms.BlockAsync(null!), () => c.Rooms.UnblockAsync(null!), () => c.Contracts.CreateAsync(null!),
            () => c.Contracts.ActivateAsync(null!), () => c.Contracts.ExtendAsync(null!), () => c.Contracts.TerminateAsync(null!)];
        foreach (var call in calls) await Assert.ThrowsAsync<ForbiddenException>(call);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(2)]
    public async Task InvalidMoneyNeverOpensTransaction(int kind)
    {
        var c = Client(Position.Manager);
        var price = kind switch { 0 => 0m, 1 => .1m, -1 => -1m, _ => 1000000000000000000m };
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.AddPriceAsync(new(1, new(2099, 1, 1), price, 1, 1, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.UpdatePriceAsync(new(1, new(2099, 1, 1), 1, price, 1, new byte[8])));
    }
    [Theory]
    [InlineData(0, null)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public async Task InvalidCapacityNeverOpensTransaction(int guests, int? area)
    {
        var c = Client(Position.Manager);
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.CreateTypeAsync(new(1, 1, "Type", guests, area, null, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.UpdateTypeAsync(new(1, 1, "Type", guests, area, null, new byte[8])));
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("12\t34")]
    [InlineData("••••1234")]
    [InlineData("１２３")]
    [InlineData("1234567890123456789012345678901")]
    public async Task RefundInvalidAccountNeverOpensTransaction(string account) =>
        await Assert.ThrowsAsync<BusinessException>(() => Client(Position.Accountant).Payments.UpdateRefundAccountAsync(new(1, account, null, new byte[8])));
    [Fact]
    public async Task MissingReasonsInvalidBlocksAndLongTextNeverOpenTransaction()
    {
        var c = Client(Position.Manager);
        await Assert.ThrowsAsync<BusinessException>(() => c.Contracts.TerminateAsync(new(1, " ", new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.UnblockAsync(new(1, new string('x', 501), new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.BlockAsync(new(1, new(2099, 1, 1), new(2099, 1, 1), RoomBlockReason.Maintenance, null, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.BlockAsync(new(1, new(2099, 1, 1), new(2099, 1, 2), (RoomBlockReason)99, null, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Rooms.CreateRoomAsync(new(1, new string('x', 21), null, new byte[8])));
        await Assert.ThrowsAsync<BusinessException>(() => c.Contracts.CreateAsync(new(1, "Số Unicode", new(2099, 1, 1), new(2099, 1, 2), .12m, new byte[8])));
    }
}
