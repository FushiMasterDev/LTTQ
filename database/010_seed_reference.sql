SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_PADDING ON;
GO
SET ANSI_WARNINGS ON;
GO
SET ARITHABORT ON;
GO
SET CONCAT_NULL_YIELDS_NULL ON;
GO
SET NUMERIC_ROUNDABORT OFF;
GO
SET XACT_ABORT ON;
GO
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 10)
    SET NOEXEC ON;
GO
-- SQLCMD: truyền mật khẩu từ ngoài; dấu nháy đơn trong giá trị phải được viết thành hai dấu nháy.
-- Thủ tục tạm chỉ tồn tại trong phiên, không thêm đối tượng vào dbo.
CREATE PROCEDURE #SeedPassword10
    @Password NVARCHAR(MAX),
    @Encoded VARCHAR(255) OUTPUT,
    @Salt VARBINARY(16) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Key VARBINARY(MAX) = CONVERT(VARBINARY(MAX),
        CONVERT(VARCHAR(MAX), @Password COLLATE Latin1_General_100_CI_AS_SC_UTF8));
    IF DATALENGTH(@Key) > 64 SET @Key = HASHBYTES('SHA2_256', @Key);
    WHILE DATALENGTH(@Key) < 64 SET @Key = @Key + 0x00;
    DECLARE @Ipad VARBINARY(MAX) = 0x, @Opad VARBINARY(MAX) = 0x, @Offset INT = 1;
    WHILE @Offset <= 64
    BEGIN
        SET @Ipad = @Ipad + CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Key, @Offset, 8))
            ^ CONVERT(BIGINT, 0x3636363636363636));
        SET @Opad = @Opad + CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Key, @Offset, 8))
            ^ CONVERT(BIGINT, 0x5C5C5C5C5C5C5C5C));
        SET @Offset += 8;
    END;
    IF @Salt IS NULL SET @Salt = CRYPT_GEN_RANDOM(16);
    IF DATALENGTH(@Salt) <> 16 THROW 51000, N'Salt phải đủ 16 byte.', 1;
    -- PBKDF2 một khối: INT(1) big-endian, U1 rồi XOR U2..U1000.
    DECLARE @U VARBINARY(32) = HASHBYTES('SHA2_256', @Opad
        + HASHBYTES('SHA2_256', @Ipad + @Salt + 0x00000001));
    DECLARE @Result VARBINARY(32) = @U, @Round INT = 2;
    WHILE @Round <= 1000
    BEGIN
        SET @U = HASHBYTES('SHA2_256', @Opad + HASHBYTES('SHA2_256', @Ipad + @U));
        SET @Result =
            CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Result, 1, 8)) ^ CONVERT(BIGINT, SUBSTRING(@U, 1, 8)))
          + CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Result, 9, 8)) ^ CONVERT(BIGINT, SUBSTRING(@U, 9, 8)))
          + CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Result, 17, 8)) ^ CONVERT(BIGINT, SUBSTRING(@U, 17, 8)))
          + CONVERT(BINARY(8), CONVERT(BIGINT, SUBSTRING(@Result, 25, 8)) ^ CONVERT(BIGINT, SUBSTRING(@U, 25, 8)));
        SET @Round += 1;
    END;
    DECLARE @Salt64 VARCHAR(24), @Hash64 VARCHAR(44);
    SELECT @Salt64 = CAST(N'' AS XML).value('xs:base64Binary(sql:variable("@Salt"))', 'varchar(24)'),
           @Hash64 = CAST(N'' AS XML).value('xs:base64Binary(sql:variable("@Result"))', 'varchar(44)');
    -- Thủ tục nằm ở tempdb: ghép ASCII bằng collation cố định, không theo server.
    SET @Encoded = 'PBKDF2-SHA256$1000$' COLLATE Latin1_General_100_BIN2
        + @Salt64 COLLATE Latin1_General_100_BIN2
        + '$' COLLATE Latin1_General_100_BIN2 + @Hash64 COLLATE Latin1_General_100_BIN2;
END;
GO
-- Vector đối chiếu hashlib.pbkdf2_hmac, có dấu và khóa UTF-8 dài hơn 64 byte.
DECLARE @Test VARCHAR(255), @Long NVARCHAR(MAX) = REPLICATE(N'MậtKhẩu123', 12);
IF CONVERT(VARBINARY(MAX), CONVERT(VARCHAR(MAX), N'MậtKhẩu123' COLLATE Latin1_General_100_CI_AS_SC_UTF8))
    <> 0x4DE1BAAD744B68E1BAA975313233
    THROW 51001, N'Chuyển mật khẩu UTF-8 không đúng.', 1;
EXEC #SeedPassword10 N'MậtKhẩu123', @Test OUTPUT, 0x000102030405060708090A0B0C0D0E0F;
IF @Test COLLATE Latin1_General_100_BIN2 <> 'PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==$rJD1TYj3W5Sdwgvkmwbmc0j4zOZYTc2x3MKZ2K4gxxA='
    THROW 51002, N'PBKDF2 không khớp vector UTF-8.', 1;
EXEC #SeedPassword10 @Long, @Test OUTPUT, 0x000102030405060708090A0B0C0D0E0F;
IF @Test COLLATE Latin1_General_100_BIN2 <> 'PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==$JCXMwgNbs+DNv2UxW+0CYqcNau0FIyvt2eHNToLjtIg='
    THROW 51003, N'HMAC không khớp vector khóa dài.', 1;
GO
BEGIN TRANSACTION;
GO
SET NOCOUNT ON;
DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(), @Hash VARCHAR(255), @UserId INT;
EXEC #SeedPassword10 N'$(FirstManagerPassword)', @Hash OUTPUT;
INSERT INTO dbo.Users (Username, PasswordHash, Status, MustChangePassword, CreatedAt)
VALUES ('NV0001', @Hash, 'Active', 1, @Now);
SET @UserId = CONVERT(INT, SCOPE_IDENTITY());
INSERT INTO dbo.Employees (UserId, EmployeeCode, FullName, Phone, Position, EmploymentStatus, HasAllAreas, CreatedAt)
VALUES (@UserId, 'NV0001', N'Quản lý đầu tiên (cần cập nhật hồ sơ)', '+84900000001', 'Manager', 'Active', 1, @Now);
GO
INSERT INTO dbo.Areas (Name, Province, AreaGroup, IsActive)
VALUES (N'Tây Hồ', N'Hà Nội', 'InnerCity', 1), (N'Ba Đình', N'Hà Nội', 'InnerCity', 1),
       (N'Sóc Sơn', N'Hà Nội', 'Suburb', 1);
INSERT INTO dbo.Amenities (Name, IsActive)
VALUES (N'Wi-Fi', 1), (N'Điều hòa', 1), (N'Bếp', 1), (N'Máy giặt', 1),
       (N'Bãi đỗ xe', 1), (N'Hồ bơi', 1), (N'Ban công', 1), (N'Khu nướng BBQ', 1);
INSERT INTO dbo.RoomCategories (Name, IsActive)
VALUES (N'Phòng đơn', 1), (N'Phòng đôi', 1), (N'Phòng gia đình', 1), (N'Nguyên căn', 1), (N'Phòng tập thể', 1);
GO
-- Ngày lễ tính giá: ngày lễ và ngày nghỉ bù đã chọn, không tự thêm cuối tuần/ngày hoán đổi làm việc.
-- Quy đổi âm lịch: 01/01 âm = 17/02/2026, 06/02/2027; 10/03 âm = 26/04/2026, 16/04/2027.
-- Nguồn 2026: https://baochinhphu.vn/bo-noi-vu-thong-bao-lich-nghi-tet-am-lich-va-nghi-le-quoc-khanh-nam-2026-102251017095507785.htm
-- Giỗ Tổ: https://xaydungchinhsach.chinhphu.vn/lich-nghi-le-gio-to-hung-vuong-30-4-1-5-quoc-khanh-2-9-nam-2026-119260222115822151.htm
-- Nguồn 2027: https://baochinhphu.vn/nghi-tet-am-lich-2027-keo-dai-7-ngay-nghi-quoc-khanh-4-ngay-102261010095648278.htm
-- Đối chiếu: https://en.baochinhphu.vn/viet-nam-announces-2027-public-holiday-schedule-111261012155048477.htm
-- Ngày Văn hóa 2026: https://baochinhphu.vn/pho-thu-tuong-quyet-dinh-phuong-an-nghi-ngay-van-hoa-tet-am-lich-quoc-khanh-10226100217552059.htm
-- Quốc khánh chọn 01–02/09/2026 và 02–03/09/2027; Quản lý có thể sửa lịch tính giá theo cơ sở.
INSERT INTO dbo.Holidays ([Date], Name)
VALUES ('20260101', N'Tết Dương lịch 2026'),
       ('20260216', N'Tết Bính Ngọ - 29 tháng Chạp'), ('20260217', N'Tết Bính Ngọ - mùng 1'),
       ('20260218', N'Tết Bính Ngọ - mùng 2'), ('20260219', N'Tết Bính Ngọ - mùng 3'),
       ('20260220', N'Tết Bính Ngọ - mùng 4'),
       ('20260426', N'Giỗ Tổ Hùng Vương - 10/03 âm lịch'), ('20260427', N'Nghỉ bù Giỗ Tổ Hùng Vương'),
       ('20260430', N'Ngày Thống nhất'), ('20260501', N'Quốc tế Lao động'),
       ('20260901', N'Nghỉ lễ Quốc khánh'), ('20260902', N'Quốc khánh'),
       ('20261124', N'Ngày Văn hóa Việt Nam'),
       ('20270101', N'Tết Dương lịch 2027'),
       ('20270204', N'Tết Đinh Mùi - 28 tháng Chạp'), ('20270205', N'Tết Đinh Mùi - 29 tháng Chạp'),
       ('20270206', N'Tết Đinh Mùi - mùng 1'), ('20270207', N'Tết Đinh Mùi - mùng 2'),
       ('20270208', N'Tết Đinh Mùi - mùng 3'), ('20270209', N'Tết Đinh Mùi - nghỉ bù mùng 4'),
       ('20270210', N'Tết Đinh Mùi - nghỉ bù mùng 5'),
       ('20270416', N'Giỗ Tổ Hùng Vương - 10/03 âm lịch'),
       ('20270430', N'Ngày Thống nhất'), ('20270501', N'Quốc tế Lao động'),
       ('20270503', N'Nghỉ bù Quốc tế Lao động'),
       ('20270902', N'Quốc khánh'), ('20270903', N'Nghỉ lễ Quốc khánh'),
       ('20271124', N'Ngày Văn hóa Việt Nam');
GO
INSERT INTO dbo.SystemSettings (SettingKey, SettingValue, Description)
VALUES ('Booking.HoldMinutes', N'15', N'Số phút giữ phòng khi chờ thanh toán'),
       ('Booking.ConfirmMinutes', N'30', N'Số phút chờ chủ xác nhận sau khi có cọc'),
       ('Booking.DepositRate', N'0.30', N'Tỷ lệ tiền cọc trên tổng tiền phòng'),
       ('Booking.MaxAwaitingPerCustomer', N'3', N'Số đơn chờ thanh toán tối đa của một khách'),
       ('Booking.MaxReceiptRejections', N'2', N'Số lần từ chối chứng từ trước khi hết hạn đơn'),
       ('Booking.MaxNights', N'31', N'Số đêm tối đa của một đơn'),
       ('Change.MinDaysBefore', N'3', N'Số ngày tối thiểu trước nhận phòng để đổi đơn'),
       ('Change.MaxPerBooking', N'2', N'Số lần đổi tối đa trên một đơn'),
       ('Change.WindowDays', N'90', N'Khoảng ngày cho phép dời lịch'),
       ('Contract.MinCommission', N'0.10', N'Tỷ lệ hoa hồng hợp đồng tối thiểu'),
       ('Contract.MaxCommission', N'0.15', N'Tỷ lệ hoa hồng hợp đồng tối đa'),
       ('Settlement.GraceDays', N'2', N'Số ngày chờ sau cuối kỳ trước khi lập đối soát'),
       ('Voucher.IssueRate', N'0.05', N'Tỷ lệ cấp voucher trên tổng tiền phòng'),
       ('Voucher.MinAmount', N'50000', N'Mệnh giá voucher tối thiểu, đồng'),
       ('Voucher.MaxAmount', N'200000', N'Mệnh giá voucher tối đa, đồng'),
       ('Voucher.ValidMonths', N'6', N'Số tháng hiệu lực của voucher'),
       ('Voucher.MaxShareOfCommission', N'0.20', N'Tỷ lệ hoa hồng tối đa dùng giảm bằng voucher'),
       ('Payment.ReviewAlertWorkingHours', N'2', N'Số giờ làm việc trước khi cảnh báo phiếu chưa xác minh'),
       ('Session.IdleMinutes', N'30', N'Số phút không thao tác trước khi hết phiên'),
       ('Login.MaxFailed', N'5', N'Số lần đăng nhập sai trước khi khóa tạm'),
       ('Login.LockoutMinutes', N'15', N'Số phút khóa tạm khi đăng nhập sai'),
       ('Fuji.BankName', N'(chưa nhập)', N'Tên ngân hàng nhận cọc, cần cấu hình MB Bank khi cài thật'),
       ('Fuji.BankAccountNo', N'(chưa nhập)', N'Số tài khoản nhận cọc, giá trị giữ chỗ không dùng chuyển tiền'),
       ('Fuji.BankAccountName', N'(chưa nhập)', N'Tên chủ tài khoản nhận cọc, cần cập nhật khi cài thật'),
       ('WorkingHours.Start', N'08:00', N'Giờ bắt đầu làm việc từ thứ Hai đến thứ Sáu, giờ Việt Nam'),
       ('WorkingHours.End', N'17:00', N'Giờ kết thúc làm việc từ thứ Hai đến thứ Sáu, giờ Việt Nam');
GO
DECLARE @Policies TABLE (Id INT);
-- Spec chưa có mốc Holiday riêng: dùng cùng mốc All, có thể chỉnh sau khi triển khai.
INSERT INTO dbo.CancellationPolicies (HomestayId, Scope, Name, IsActive)
OUTPUT INSERTED.Id INTO @Policies
VALUES (NULL, 'All', N'Chính sách chung ngày thường', 1),
       (NULL, 'Holiday', N'Chính sách chung ngày lễ', 1);
INSERT INTO dbo.CancellationRules (PolicyId, MinDaysBefore, RefundPercent)
SELECT p.Id, r.MinDays, r.RefundPercent
FROM @Policies AS p CROSS JOIN (VALUES (7, 100), (3, 50), (0, 0)) AS r(MinDays, RefundPercent);
GO
DROP PROCEDURE #SeedPassword10;
GO
INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (10, N'010_seed_reference.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO
