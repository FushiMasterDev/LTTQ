using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử M4.2A: dữ liệu nhập chủ nhà/homestay/tập tin sai bị chặn trước khi mở giao dịch.</summary>
public sealed class M42AInputContractTests
{
    private static (OwnerService Owners, HomestayService Homes) Client(Position position = Position.Manager)
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { Position = position });
        // No transaction or connection is opened by these precondition failures; use the production validator.
        var factory = new ConnectionFactory("Server=invalid.example;Database=unused;Integrated Security=true;Connect Timeout=1", 1);
        var authorizer = new Authorizer(current); var scope = new DataScope(current);
        return (new(authorizer, scope, factory, new OwnerRepository()), new(authorizer, current, scope, factory, new HomestayRepository(), new DbClock(factory)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task OwnerInvalidInputIsRejectedBeforeTransaction(int kind)
    {
        var c = Client(); var input = new CreateOwnerRequest { FullName = "Chủ", Phone = "0912345678" };
        input = kind switch { 0 => input with { FullName = " " }, 1 => input with { Phone = "invalid" }, 2 => input with { Email = "not an email" },
            3 => input with { IdNumber = "••1234" }, 4 => input with { BankAccountNo = "123\n456" }, _ => input with { FullName = new string('x', 101) } };
        await Assert.ThrowsAsync<BusinessException>(() => c.Owners.CreateAsync(input));
    }
    [Fact]
    public async Task FileMagicAndSizeAreCheckedBeforeAnyTransaction()
    {
        var c = Client();
        foreach (var bytes in new[] { new byte[] { 0x4d, 0x5a }, new byte[ReceiptRules.MaxEvidenceBytes + 1], Array.Empty<byte>() })
        {
            await Assert.ThrowsAsync<BusinessException>(() => c.Homes.AddImageAsync(new(1, bytes, "fake.jpg", null, null, 0, true, new byte[8])));
            await Assert.ThrowsAsync<BusinessException>(() => c.Homes.AddDocumentAsync(new(1, DocumentType.BusinessLicense, bytes, "fake.pdf", null, new byte[8])));
        }
    }
    [Fact]
    public async Task PolicyErrorUsesExistingValidatorMessageBeforeTransaction()
    {
        var c = Client(); var policy = new CreateCancellationPolicyRequest { Name = "Policy", Rules = [new(3, 50)] };
        var expected = Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate(policy.Rules));
        var actual = await Assert.ThrowsAsync<BusinessException>(() => c.Homes.SetCancellationPolicyAsync(new(1, policy, new byte[8])));
        Assert.Equal(expected.Message, actual.Message);
    }
    [Fact]
    public async Task AccountantHasNoWriteAccessEvenWhenArgumentsAreNull()
    {
        var c = Client(Position.Accountant);
        Func<Task>[] writes = [ () => c.Owners.CreateAsync(null!), () => c.Owners.UpdateAsync(null!), () => c.Owners.ChangeBankAsync(null!), () => c.Owners.ChangeStatusAsync(null!),
            () => c.Homes.CreateDraftAsync(null!), () => c.Homes.UpdateAsync(null!), () => c.Homes.SetAmenitiesAsync(null!), () => c.Homes.AddImageAsync(null!),
            () => c.Homes.UpdateImageAsync(null!), () => c.Homes.RemoveImageAsync(null!), () => c.Homes.AddDocumentAsync(null!), () => c.Homes.RemoveDocumentAsync(null!),
            () => c.Homes.SubmitAsync(null!), () => c.Homes.ApproveAsync(null!), () => c.Homes.ReturnAsync(null!), () => c.Homes.SuspendAsync(null!),
            () => c.Homes.ReopenAsync(null!), () => c.Homes.EndAsync(null!), () => c.Homes.SetCancellationPolicyAsync(null!), () => c.Homes.DisableCancellationPolicyAsync(null!) ];
        Assert.Equal(20, writes.Length);
        foreach (var write in writes) await Assert.ThrowsAsync<ForbiddenException>(write);
    }
}
