using System.Security.Cryptography;
using System.Text;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.Rules.Tests;

public sealed class M24RulesTests
{
    // Independent transcription of spec 7.2, including creation and internal-only rollback after applying changes.
    private static readonly (BookingStatus? From, BookingStatus To, string Actors, bool Behalf)[] Table =
    [
        (null, BookingStatus.AwaitingPayment, "Staff,Manager", false),
        (BookingStatus.AwaitingPayment, BookingStatus.PaymentSubmitted, "Staff,Manager,Accountant", false),
        (BookingStatus.AwaitingPayment, BookingStatus.Deposited, "Accountant", false),
        (BookingStatus.AwaitingPayment, BookingStatus.Expired, "System", false),
        (BookingStatus.AwaitingPayment, BookingStatus.Cancelled, "Staff,Manager", false),
        (BookingStatus.PaymentSubmitted, BookingStatus.Deposited, "Accountant", false),
        (BookingStatus.PaymentSubmitted, BookingStatus.AwaitingPayment, "Accountant", false),
        (BookingStatus.PaymentSubmitted, BookingStatus.Expired, "Accountant", false),
        (BookingStatus.Deposited, BookingStatus.Confirmed, "Staff,Manager", true),
        (BookingStatus.Deposited, BookingStatus.Rejected, "Staff,Manager", true),
        (BookingStatus.Deposited, BookingStatus.Cancelled, "Manager", false),
        (BookingStatus.Confirmed, BookingStatus.CheckedIn, "Staff,Manager", true),
        (BookingStatus.Confirmed, BookingStatus.NoShow, "Staff,Manager", true),
        (BookingStatus.Confirmed, BookingStatus.Cancelled, "Manager", false),
        (BookingStatus.Confirmed, BookingStatus.Deposited, "Internal", false),
        (BookingStatus.CheckedIn, BookingStatus.Completed, "Staff,Manager", true)
    ];
    public static IEnumerable<object?[]> EveryPair() => new BookingStatus?[] { null }.Concat(Enum.GetValues<BookingStatus>().Select(s => (BookingStatus?)s))
        .SelectMany(from => Enum.GetValues<BookingStatus>().Select(to => new object?[] { from, to }));

    [Theory]
    [MemberData(nameof(EveryPair))]
    public void EveryPairAndEveryActorMatchTheSpecification(BookingStatus? from, BookingStatus to)
    {
        var expected = Table.Where(t => t.From == from && t.To == to).ToArray();
        Assert.Equal(expected.Length == 1, BookingStateMachine.Find(from, to) is not null);
        foreach (var actor in Enum.GetValues<BookingTransitionActor>())
        {
            var allowed = expected.Length == 1 && expected[0].Actors.Split(',').Contains(actor.ToString());
            Assert.Equal(allowed, BookingStateMachine.CanTransition(from, to, actor));
            if (allowed)
            {
                var rule = BookingStateMachine.Require(from, to, actor, "Đã gọi chủ");
                Assert.Equal(expected[0].Behalf, rule.OnBehalfOfOwner);
                if (rule.OnBehalfOfOwner)
                {
                    Assert.Throws<BusinessException>(() => BookingStateMachine.Require(from, to, actor));
                    Assert.Throws<BusinessException>(() => BookingStateMachine.Require(from, to, actor, "  "));
                    Assert.Throws<BusinessException>(() => BookingStateMachine.Require(from, to, actor, new string('a', 501)));
                }
            }
            else Assert.ThrowsAny<BusinessException>(() => BookingStateMachine.Require(from, to, actor, "Đã gọi chủ"));
        }
    }

    [Fact]
    public void FinalStatesAndUnknownValuesCannotTransitionAndHumanScopeIsRequired()
    {
        foreach (var final in new[] { BookingStatus.Completed, BookingStatus.Expired, BookingStatus.Rejected, BookingStatus.Cancelled, BookingStatus.NoShow })
        {
            Assert.True(BookingStateMachine.IsTerminal(final));
            Assert.DoesNotContain(BookingStateMachine.Transitions, t => t.From == final);
        }
        Assert.False(BookingStateMachine.CanTransition((BookingStatus)999, BookingStatus.Deposited, BookingTransitionActor.Internal));
        Assert.False(BookingStateMachine.CanTransition(BookingStatus.AwaitingPayment, (BookingStatus)999, BookingTransitionActor.Manager));
        Assert.False(BookingStateMachine.CanTransition(null, BookingStatus.AwaitingPayment, (BookingTransitionActor)999));
        foreach (var role in new[] { Position.Staff, Position.Manager, Position.Accountant })
            Assert.Equal(role.ToString(), BookingStateMachine.Actor(role).ToString());
        Assert.False(BookingStateMachine.CanTransition(BookingStatus.AwaitingPayment, BookingStatus.Cancelled, BookingTransitionActor.Staff, false));
        Assert.False(BookingStateMachine.CanTransition(BookingStatus.AwaitingPayment, BookingStatus.Cancelled, BookingTransitionActor.Manager, false));
        Assert.True(BookingStateMachine.CanTransition(BookingStatus.AwaitingPayment, BookingStatus.PaymentSubmitted, BookingTransitionActor.Accountant, false));
    }

    public static IEnumerable<object[]> Signatures()
    {
        yield return [new byte[] { 0xFF, 0xD8, 0xFF, 1 }, "image/jpeg"];
        yield return [new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"];
        yield return [Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBP"), "image/webp"];
        yield return [Encoding.ASCII.GetBytes("%PDF-1.7"), "application/pdf"];
    }
    [Theory]
    [MemberData(nameof(Signatures))]
    public void EvidenceUsesSignatureDespiteJpgExtensionAndOwnsBytes(byte[] bytes, string mime)
    {
        var file = ReceiptRules.ValidateEvidence(bytes, "C:\\local\\renamed.jpg")!;
        Assert.Equal(mime, file.ContentType); Assert.Equal("renamed.jpg", file.FileName);
        Assert.Equal(SHA256.HashData(bytes), file.Sha256); Assert.Equal(bytes, file.Content);
        bytes[0] = 0; Assert.NotEqual(bytes[0], file.Content[0]);
    }
    [Fact]
    public void EmptyOversizedExecutableTextAndWrongWebpSignaturesAreRejected()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), "MZ executable"u8.ToArray(), "just text"u8.ToArray(),
            "RIFF1234WAVE"u8.ToArray(), "WEBP"u8.ToArray(), new byte[ReceiptRules.MaxEvidenceBytes + 1] })
            Assert.Throws<BusinessException>(() => ReceiptRules.ValidateEvidence(bytes, "fake.jpg"));
        Assert.Null(ReceiptRules.ValidateEvidence(null, null));
        var max = new byte[ReceiptRules.MaxEvidenceBytes]; "%PDF-"u8.CopyTo(max);
        Assert.Equal(max.Length, ReceiptRules.ValidateEvidence(max, "max.pdf")!.Content.Length);
    }
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1234567890123456", "••••3456")]
    [InlineData("1234", "••••1234")]
    [InlineData("12", "••••12")]
    public void AccountMaskRetainsOnlyFourTrailingCharacters(string? input, string? expected)
        => Assert.Equal(expected, ReceiptRules.MaskAccount(input));

    [Fact]
    public void ReceiptTimesAmountsLengthsAndCounterFollowServerContract()
    {
        var now = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc);
        RecordReceiptRequest request = new() { BookingId = 1, RowVer = new byte[8], Amount = 1, Method = PaymentMethod.Cash };
        Assert.Equal(now, ReceiptRules.TransferredAt(ReceiptRules.Normalize(request), now));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { Method = PaymentMethod.BankTransfer }));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { TransferredAtUtc = DateTime.SpecifyKind(now, DateTimeKind.Unspecified) }));
        Assert.Equal(now.AddMinutes(5), ReceiptRules.TransferredAt(request with { TransferredAtUtc = now.AddMinutes(5) }, now));
        Assert.Throws<BusinessException>(() => ReceiptRules.TransferredAt(request with { TransferredAtUtc = now.AddMinutes(5).AddMilliseconds(1) }, now));
        foreach (var amount in new[] { 0, -1, .5m, 1000000000000000000m })
            Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { Amount = amount }));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { PayerName = new string('n', 101) }));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { PayerAccount = new string('1', 31) }));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { TransferContent = new string('n', 256) }));
        Assert.Throws<BusinessException>(() => ReceiptRules.Normalize(request with { Method = (PaymentMethod)999 }));
        Assert.Equal("PT2701", ReceiptRules.CounterKey(now)); Assert.Equal("PT2701-0001", ReceiptRules.VoucherNo(now, 1));
        Assert.Equal("PT2701-10000", ReceiptRules.VoucherNo(now, 10000));
    }
}
