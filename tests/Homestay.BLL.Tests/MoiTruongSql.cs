using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Tests;

/// <summary>Kết nối tới CSDL kiểm thử (biến môi trường HOMESTAY_TEST_CONNECTION) và tạo nhanh dữ liệu đơn mẫu.</summary>
internal static class SqlFixture
{
    public static ConnectionFactory Factory() => new(Environment.GetEnvironmentVariable("HOMESTAY_TEST_CONNECTION")!, 30);
    public static CancellationToken Token => CancellationToken.None;

    public static async Task<(int BookingId, int RoomId, int UserId, int EmployeeId, int HomestayId)> CreateBookingAsync(UnitOfWork uow, CancellationToken cancellationToken)
    {
        using var command = uow.CreateCommand("""
            DECLARE @user int = (SELECT TOP (1) Id FROM dbo.Users ORDER BY Id);
            DECLARE @employee int = (SELECT TOP (1) Id FROM dbo.Employees WHERE UserId=@user ORDER BY Id);
            DECLARE @area int = (SELECT TOP (1) Id FROM dbo.Areas ORDER BY Id);
            DECLARE @category int = (SELECT TOP (1) Id FROM dbo.RoomCategories ORDER BY Id);
            DECLARE @policy int = (SELECT TOP (1) Id FROM dbo.CancellationPolicies WHERE HomestayId IS NULL AND IsActive=1 ORDER BY Id);
            INSERT dbo.Owners (FullName, Phone, Status) VALUES (N'Chủ kiểm thử', @phone, 'Pending');
            DECLARE @owner int = SCOPE_IDENTITY();
            INSERT dbo.Homestays (OwnerId, AreaId, Name, Address, Status, CreatedByUserId)
            VALUES (@owner, @area, N'Homestay kiểm thử', N'Địa chỉ kiểm thử', 'Draft', @user);
            DECLARE @homestay int = SCOPE_IDENTITY();
            INSERT dbo.RoomTypes (HomestayId, CategoryId, Name, MaxGuests, IsActive)
            VALUES (@homestay, @category, N'Phòng kiểm thử', 2, 1);
            DECLARE @type int = SCOPE_IDENTITY();
            INSERT dbo.Rooms (RoomTypeId, RoomCode, IsActive) VALUES (@type, N'T01', 1);
            DECLARE @room int = SCOPE_IDENTITY();
            INSERT dbo.Customers (FullName, Phone, CreatedByEmployeeId) VALUES (N'Khách kiểm thử', @phone, @employee);
            DECLARE @customer int = SCOPE_IDENTITY();
            INSERT dbo.Bookings (Code, CustomerId, ContactName, ContactPhone, SourceChannel, CreatedByEmployeeId,
                HomestayId, RoomTypeId, RoomId, CheckIn, CheckOut, Guests, Status, HoldExpiresAt,
                TotalAmount, DepositRate, DepositAmount, CommissionRate, CancellationPolicyId)
            VALUES (@code, @customer, N'Khách kiểm thử', @phone, 'Phone', @employee,
                @homestay, @type, @room, '2099-01-01', '2099-01-02', 2, 'AwaitingPayment', DATEADD(minute, 15, SYSUTCDATETIME()),
                1000000, 0.3, 300000, 0.1, @policy);
            SELECT CAST(SCOPE_IDENTITY() AS int) AS BookingId, @room AS RoomId, @user AS UserId, @employee AS EmployeeId, @homestay AS HomestayId;
            """);
        command.Parameters.AddVarChar("@code", 20, "T" + Guid.NewGuid().ToString("N")[..18]);
        command.Parameters.AddVarChar("@phone", 16, "+" + Random.Shared.NextInt64(100000000000, 999999999999));
        var rows = await uow.QueryAsync(command, r => (r.GetInt32("BookingId"), r.GetInt32("RoomId"), r.GetInt32("UserId"), r.GetInt32("EmployeeId"), r.GetInt32("HomestayId")), cancellationToken);
        return Assert.Single(rows);
    }
}
