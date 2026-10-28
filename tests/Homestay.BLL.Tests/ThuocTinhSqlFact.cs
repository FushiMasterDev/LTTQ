namespace Homestay.BLL.Tests;

/// <summary>Đánh dấu kiểm thử cần SQL Server; tự bỏ qua khi chưa cấu hình CSDL kiểm thử.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOMESTAY_TEST_CONNECTION")))
            Skip = "Chưa đặt HOMESTAY_TEST_CONNECTION; cần SQL Server 2022 đã chạy schema và seed.";
    }
}
