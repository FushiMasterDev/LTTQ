using Homestay.DTO.Availability;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

// Đối chiếu chéo: seed tính giá bằng T-SQL, báo giá tính bằng PricingCalculator C# — hai cài đặt độc lập phải ra cùng số.
/// <summary>Kiểm thử đối chiếu: báo giá tính bằng C# khớp với giá các đơn mẫu tính bằng SQL.</summary>
public sealed class SeedPriceCrossCheckTests
{
    /// <summary>Một đơn mẫu trong dữ liệu seed dùng để đối chiếu giá.</summary>
    private sealed record SeedBooking(string Code, int RoomTypeId, DateOnly CheckIn, DateOnly CheckOut, int Guests,
        decimal TotalAmount, decimal DepositAmount, IReadOnlyList<(DateOnly Date, string DayType, decimal Price)> Nights);

    [SqlFact]
    public async Task QuoteMatchesSeedBookingsPricedBySql()
    {
        var client = new M21TestClient();
        await client.LoginNewAsync(Position.Manager);
        var seeds = await client.Factory.ReadAsync(async (db, ct) =>
        {
            using var command = db.CreateCommand("""
                SELECT b.Id, b.Code, b.RoomTypeId, b.CheckIn, b.CheckOut, b.Guests, b.TotalAmount, b.DepositAmount
                FROM dbo.Bookings b
                WHERE EXISTS (SELECT 1 FROM dbo.AuditLogs a WHERE a.TableName = 'Bookings' AND a.RecordId = CAST(b.Id AS VARCHAR(50)) AND a.MachineName = N'SEED')
                  AND b.CheckIn >= CAST(SWITCHOFFSET(SYSUTCDATETIME(), '+07:00') AS DATE)
                ORDER BY b.Id;
                """);
            var rows = await db.QueryAsync(command, r => (Id: r.GetInt32(0), Code: r.GetString(1), RoomTypeId: r.GetInt32(2),
                CheckIn: DateOnly.FromDateTime(r.GetDateTime(3)), CheckOut: DateOnly.FromDateTime(r.GetDateTime(4)),
                Guests: r.GetInt32(5), Total: r.GetDecimal(6), Deposit: r.GetDecimal(7)), ct);
            var result = new List<SeedBooking>();
            foreach (var row in rows)
            {
                using var nights = db.CreateCommand("SELECT [Date], DayType, Price FROM dbo.BookingNights WHERE BookingId = @id ORDER BY [Date];");
                nights.Parameters.AddWithValue("@id", row.Id);
                var list = await db.QueryAsync(nights, r => (DateOnly.FromDateTime(r.GetDateTime(0)), r.GetString(1), r.GetDecimal(2)), ct);
                result.Add(new(row.Code, row.RoomTypeId, row.CheckIn, row.CheckOut, row.Guests, row.Total, row.Deposit, list));
            }
            return result;
        }, CancellationToken.None);

        Assert.NotEmpty(seeds);
        foreach (var seed in seeds)
        {
            var quote = await client.Availability.QuoteAsync(new QuoteRequest(seed.RoomTypeId, seed.CheckIn, seed.CheckOut, seed.Guests));
            Assert.True(seed.TotalAmount == quote.Pricing.TotalAmount, $"{seed.Code}: tổng SQL {seed.TotalAmount} ≠ C# {quote.Pricing.TotalAmount}");
            Assert.True(seed.DepositAmount == quote.Pricing.DepositAmount, $"{seed.Code}: cọc SQL {seed.DepositAmount} ≠ C# {quote.Pricing.DepositAmount}");
            Assert.Equal(seed.Nights.Select(n => (n.Date, n.DayType, n.Price)),
                quote.Pricing.Nights.Select(n => (n.Date, n.DayType.ToString(), n.Price)));
        }
    }
}
