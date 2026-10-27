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
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 20)
    SET NOEXEC ON;
GO
-- SQLCMD: truyền mật khẩu từ ngoài; dấu nháy đơn trong giá trị phải được viết thành hai dấu nháy.
-- Thủ tục tạm chỉ tồn tại trong phiên, không thêm đối tượng vào dbo.
CREATE PROCEDURE #SeedPassword20
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
EXEC #SeedPassword20 N'MậtKhẩu123', @Test OUTPUT, 0x000102030405060708090A0B0C0D0E0F;
IF @Test COLLATE Latin1_General_100_BIN2 <> 'PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==$rJD1TYj3W5Sdwgvkmwbmc0j4zOZYTc2x3MKZ2K4gxxA='
    THROW 51002, N'PBKDF2 không khớp vector UTF-8.', 1;
EXEC #SeedPassword20 @Long, @Test OUTPUT, 0x000102030405060708090A0B0C0D0E0F;
IF @Test COLLATE Latin1_General_100_BIN2 <> 'PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==$JCXMwgNbs+DNv2UxW+0CYqcNau0FIyvt2eHNToLjtIg='
    THROW 51003, N'HMAC không khớp vector khóa dài.', 1;
GO
BEGIN TRANSACTION;
GO
SET NOCOUNT ON;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 10)
    THROW 51010, N'Phải chạy 010_seed_reference.sql trước dữ liệu mẫu.', 1;
CREATE TABLE #Context (NowUtc DATETIME2(3), Today DATE, PeriodEnd DATE, ManagerId INT, ManagerUserId INT);
DECLARE @Now DATETIME2(3) = SYSUTCDATETIME(), @Today DATE, @End DATE;
SET @Today = CONVERT(DATE, DATEADD(HOUR, 7, @Now));
SET @End = CASE WHEN DAY(@Today) > 15 THEN DATEFROMPARTS(YEAR(@Today), MONTH(@Today), 15)
                ELSE EOMONTH(@Today, -1) END;
WHILE DATEADD(DAY, 2, @End) >= @Today
    SET @End = CASE WHEN DAY(@End) = 15 THEN EOMONTH(@End, -1)
                    ELSE DATEFROMPARTS(YEAR(@End), MONTH(@End), 15) END;
INSERT INTO #Context
SELECT @Now, @Today, @End, e.Id, e.UserId FROM dbo.Employees AS e WHERE e.EmployeeCode = 'NV0001';
IF (SELECT COUNT(*) FROM #Context) <> 1 THROW 51011, N'Không tìm thấy Quản lý NV0001.', 1;
-- Cột chuỗi trong bảng tạm dùng collation database nghiệp vụ thay vì tempdb/server.
CREATE TABLE #Employees (Code VARCHAR(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, Id INT, UserId INT);
INSERT INTO #Employees SELECT 'NV0001', ManagerId, ManagerUserId FROM #Context;
DECLARE @Accounts TABLE (N INT, FullName NVARCHAR(100) COLLATE DATABASE_DEFAULT,
    Position VARCHAR(20) COLLATE DATABASE_DEFAULT);
INSERT INTO @Accounts VALUES
    (2, N'Nguyễn An Mẫu', 'Staff'), (3, N'Trần Bình Mẫu', 'Staff'), (4, N'Lê Chi Mẫu', 'Staff'),
    (5, N'Phạm Dũng Mẫu', 'Manager'), (6, N'Hoàng Hà Mẫu', 'Accountant'), (7, N'Vũ Lan Mẫu', 'Accountant');
DECLARE @N INT = 2, @Code VARCHAR(20), @Hash VARCHAR(255), @UserId INT;
WHILE @N <= 7
BEGIN
    SET @Code = 'NV' + RIGHT('0000' + CONVERT(VARCHAR(4), @N), 4);
    EXEC #SeedPassword20 N'$(DemoPassword)', @Hash OUTPUT;
    INSERT INTO dbo.Users (Username, PasswordHash, Status, MustChangePassword, LockReason, CreatedAt)
    VALUES (@Code, @Hash, CASE WHEN @N = 4 THEN 'Locked' ELSE 'Active' END,
            CASE WHEN @N = 3 THEN 1 ELSE 0 END,
            CASE WHEN @N = 4 THEN N'Tài khoản mẫu bị khóa để minh họa quản lý truy cập' END, @Now);
    SET @UserId = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO dbo.Employees (UserId, EmployeeCode, FullName, Phone, Email, Position, EmploymentStatus, HasAllAreas, CreatedAt)
    OUTPUT INSERTED.EmployeeCode, INSERTED.Id, INSERTED.UserId INTO #Employees
    SELECT @UserId, @Code, a.FullName, '+8490000000' + CONVERT(VARCHAR(1), @N),
           @Code + N'@example.invalid', a.Position, 'Active', 0, @Now FROM @Accounts AS a WHERE a.N = @N;
    SET @N += 1;
END;
INSERT INTO dbo.EmployeeAreas (EmployeeId, AreaId, AssignedByEmployeeId, AssignedAt)
SELECT e.Id, a.Id, c.ManagerId, DATEADD(DAY, -60, c.NowUtc)
FROM #Employees AS e CROSS JOIN dbo.Areas AS a CROSS JOIN #Context AS c
WHERE (e.Code IN ('NV0002', 'NV0005') AND a.Name IN (N'Tây Hồ', N'Ba Đình'))
   OR (e.Code = 'NV0003' AND a.Name = N'Sóc Sơn')
   OR e.Code IN ('NV0006', 'NV0007');
-- NV0004 không gán khu vực; kế toán có đủ ba khu vực để minh họa bộ lọc.
GO
CREATE TABLE #Owners (N INT PRIMARY KEY, Id INT);
DECLARE @N INT = 1, @Id INT;
WHILE @N <= 4
BEGIN
    INSERT INTO dbo.Owners (FullName, Phone, Email, Address, IdNumber, BankName, BankAccountNo,
        BankAccountName, Status, ApprovedByEmployeeId, ApprovedAt, Note, CreatedByEmployeeId, CreatedAt)
    SELECT CHOOSE(@N, N'Nguyễn Mai Mẫu', N'Trần Nam Mẫu', N'Lê Hương Mẫu', N'Phạm Sơn Mẫu'),
           '+8490000010' + CONVERT(VARCHAR(1), @N), N'chu' + CONVERT(NVARCHAR(1), @N) + N'@example.invalid',
           N'Địa chỉ giả số ' + CONVERT(NVARCHAR(1), @N) + N', Hà Nội',
           '00000000010' + CONVERT(VARCHAR(1), @N), N'Ngân hàng giả - không chuyển tiền',
           '00000000000' + CONVERT(VARCHAR(1), @N), N'CHU MAU ' + CONVERT(NVARCHAR(1), @N),
           CASE WHEN @N = 4 THEN 'Pending' ELSE 'Approved' END,
           CASE WHEN @N < 4 THEN c.ManagerId END, CASE WHEN @N < 4 THEN DATEADD(DAY, -100, c.NowUtc) END,
           N'DỮ LIỆU GIẢ, không dùng thông tin thanh toán này', c.ManagerId, DATEADD(DAY, -110, c.NowUtc)
    FROM #Context AS c;
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO #Owners VALUES (@N, @Id);
    SET @N += 1;
END;
CREATE TABLE #Homestays (N INT PRIMARY KEY, Id INT, StaffId INT, StaffUserId INT, ImageId UNIQUEIDENTIFIER);
DECLARE @N2 INT = 1, @H INT, @OwnerN INT, @Area NVARCHAR(100), @StaffCode VARCHAR(20);
WHILE @N2 <= 6
BEGIN
    SET @OwnerN = CASE WHEN @N2 IN (1, 4) THEN 1 WHEN @N2 = 5 THEN 4 WHEN @N2 = 6 THEN 2 ELSE @N2 END;
    SET @Area = CHOOSE(@N2, N'Tây Hồ', N'Ba Đình', N'Sóc Sơn', N'Tây Hồ', N'Ba Đình', N'Sóc Sơn');
    SET @StaffCode = CASE WHEN @Area = N'Sóc Sơn' THEN 'NV0003' ELSE 'NV0002' END;
    INSERT INTO dbo.Homestays (OwnerId, AreaId, Name, Address, ContactPhone, Description, CheckinGuide,
        Status, SubmittedAt, ApprovedByEmployeeId, ApprovedAt, CreatedByUserId, CreatedAt)
    SELECT o.Id, a.Id, CHOOSE(@N2, N'Nhà Mẫu Sen Tây Hồ', N'Nhà Mẫu Nắng Ba Đình', N'Nhà Mẫu Thông Sóc Sơn',
           N'Nhà Mẫu Mây Tây Hồ', N'Nhà Mẫu Lá Bản Nháp', N'Nhà Mẫu Gió Chờ Duyệt'),
           N'Số giả ' + CONVERT(NVARCHAR(1), @N2) + N', ngõ Dữ Liệu Mẫu, ' + @Area + N', Hà Nội',
           '+8490000020' + CONVERT(VARCHAR(1), @N2), N'Cơ sở minh họa, không tồn tại ngoài thực tế.',
           N'Gọi số liên hệ mẫu khi đến cổng. Không sử dụng địa chỉ này để đi lại.',
           CASE WHEN @N2 <= 4 THEN 'Active' WHEN @N2 = 5 THEN 'Draft' ELSE 'PendingReview' END,
           CASE WHEN @N2 <> 5 THEN DATEADD(DAY, -95, c.NowUtc) END,
           CASE WHEN @N2 <= 4 THEN c.ManagerId END, CASE WHEN @N2 <= 4 THEN DATEADD(DAY, -90, c.NowUtc) END,
           e.UserId, DATEADD(DAY, -100, c.NowUtc)
    FROM #Owners AS o JOIN dbo.Areas AS a ON a.Name = @Area AND a.Province = N'Hà Nội'
    CROSS JOIN #Context AS c JOIN #Employees AS e ON e.Code = @StaffCode WHERE o.N = @OwnerN;
    SET @H = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO #Homestays SELECT @N2, @H, Id, UserId, NEWID() FROM #Employees WHERE Code = @StaffCode;
    SET @N2 += 1;
END;
INSERT INTO dbo.HomestayAmenities (HomestayId, AmenityId)
SELECT h.Id, a.Id FROM #Homestays AS h CROSS JOIN dbo.Amenities AS a;
INSERT INTO dbo.Contracts (HomestayId, ContractNo, StartDate, EndDate, CommissionRate, Status, SignedByEmployeeId, CreatedAt)
SELECT h.Id, 'HD-MAU-' + CONVERT(VARCHAR(1), h.N), DATEADD(DAY, -90, c.Today), DATEADD(YEAR, 2, c.Today),
       CONVERT(DECIMAL(5,4), CHOOSE(h.N, 0.10, 0.12, 0.15, 0.11)), 'Active', c.ManagerId, DATEADD(DAY, -90, c.NowUtc)
FROM #Homestays AS h CROSS JOIN #Context AS c WHERE h.N <= 4;
GO
-- Ảnh PNG 32x32 RGB hợp lệ, mỗi homestay một màu; PDF có xref/trailer và một trang.
CREATE TABLE #Images (N INT PRIMARY KEY, Content VARBINARY(MAX));
INSERT INTO #Images (N, Content) VALUES
    (1, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F7220316AC66C7D0000002A49444154789C63706E29A0296218B560D482510B462D18B560D482510B462D18B560D482510B868A0500D20ADC3D9D91AA360000000049454E44AE426082),
    (2, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F722032F3CF3DC70000002A49444154789CEDCD410900000804B04B67623399C3143E84C1FECB749D8A402010080402814020F8122C3673806A0982A4FF0000000049454E44AE426082),
    (3, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F72203384C80D510000002A49444154789C63302974A3296218B560D482510B462D18B560D482510B462D18B560D482510B868A0500D20DAC2E37DCBDB00000000049454E44AE426082),
    (4, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F7220341AAC98F20000002A49444154789C63486E3A4A53C4306AC1A805A3168C5A306AC1A805A3168C5A306AC1A805A3160C150B006175A85B93E8E2400000000049454E44AE426082),
    (5, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F7220356DABA8640000002A49444154789CEDCD410900000804B04B6C264318D0143E84C1FECB549F8A402010080402814020F8122C5BAD086A22FB334A0000000049454E44AE426082),
    (6, 0x89504E470D0A1A0A0000000D4948445200000020000000200802000000FC18EDA300000060744558744465736372697074696F6E0044454D4F20484F4D4553544159202D2046494354494F4E414C2053414D504C4520494D4147452E20444F204E4F54205553452041532041205245414C2050524F50455254592050484F544F2E20436F6C6F722036F4A2F9DE0000002A49444154789C63688A77A4296218B560D482510B462D18B560D482510B462D18B560D482510B868A05002C55883DF4C633CB0000000049454E44AE426082);
DECLARE @Pdf VARBINARY(MAX) = 0x255044462D312E340A312030206F626A0A3C3C202F54797065202F436174616C6F67202F5061676573203220302052203E3E0A656E646F626A0A322030206F626A0A3C3C202F54797065202F5061676573202F4B696473205B33203020525D202F436F756E742031203E3E0A656E646F626A0A332030206F626A0A3C3C202F54797065202F50616765202F506172656E74203220302052202F4D65646961426F78205B30203020353030203138305D202F5265736F7572636573203C3C202F466F6E74203C3C202F4631203420302052203E3E203E3E202F436F6E74656E7473203520302052203E3E0A656E646F626A0A342030206F626A0A3C3C202F54797065202F466F6E74202F53756274797065202F5479706531202F42617365466F6E74202F48656C766574696361203E3E0A656E646F626A0A352030206F626A0A3C3C202F4C656E677468203731203E3E0A73747265616D0A4254202F463120313420546620333620313030205464202844454D4F20444F43554D454E54202D204E4F542056414C494420464F52204C4547414C205553452920546A2045540A656E6473747265616D0A656E646F626A0A787265660A3020360A303030303030303030302036353533352066200A30303030303030303039203030303030206E200A30303030303030303538203030303030206E200A30303030303030313135203030303030206E200A30303030303030323431203030303030206E200A30303030303030333131203030303030206E200A747261696C65720A3C3C202F53697A652036202F526F6F74203120302052203E3E0A7374617274787265660A3433310A2525454F460A;
INSERT INTO dbo.StoredFiles (Id, Category, FileName, ContentType, SizeBytes, Sha256, Content, UploadedByUserId, CreatedAt)
SELECT h.ImageId, 'HomestayImage', N'homestay-mau-' + CONVERT(NVARCHAR(1), h.N) + N'.png', 'image/png',
       DATALENGTH(i.Content), HASHBYTES('SHA2_256', i.Content), i.Content, c.ManagerUserId, DATEADD(DAY, -96, c.NowUtc)
FROM #Homestays AS h JOIN #Images AS i ON i.N = h.N CROSS JOIN #Context AS c;
INSERT INTO dbo.HomestayImages (HomestayId, FileId, Caption, SortOrder, IsCover)
SELECT Id, ImageId, N'Ảnh màu minh họa - dữ liệu giả', 0, 1 FROM #Homestays;
CREATE TABLE #Documents (HomestayId INT, DocumentType VARCHAR(30) COLLATE DATABASE_DEFAULT, FileId UNIQUEIDENTIFIER);
INSERT INTO #Documents
SELECT h.Id, d.DocumentType, NEWID() FROM #Homestays AS h
CROSS JOIN (VALUES ('BusinessLicense'), ('FireSafety')) AS d(DocumentType) WHERE h.N <= 4;
INSERT INTO dbo.StoredFiles (Id, Category, FileName, ContentType, SizeBytes, Sha256, Content, UploadedByUserId, CreatedAt)
SELECT d.FileId, 'Document', N'GIAY-TO-GIA-' + CONVERT(NVARCHAR(10), d.HomestayId) + N'-' + d.DocumentType + N'.pdf',
       'application/pdf', DATALENGTH(@Pdf), HASHBYTES('SHA2_256', @Pdf), @Pdf, c.ManagerUserId, DATEADD(DAY, -96, c.NowUtc)
FROM #Documents AS d CROSS JOIN #Context AS c;
INSERT INTO dbo.VerificationDocuments (HomestayId, DocumentType, FileId, ExpiryDate, UploadedByUserId, CreatedAt)
SELECT d.HomestayId, d.DocumentType, d.FileId, DATEADD(YEAR, 2, c.Today), c.ManagerUserId, DATEADD(DAY, -96, c.NowUtc)
FROM #Documents AS d CROSS JOIN #Context AS c;
GO
CREATE TABLE #Types (HomestayN INT, N INT, Id INT, PRIMARY KEY (HomestayN, N));
DECLARE @H INT = 1, @T INT, @Id INT;
WHILE @H <= 4
BEGIN
    SET @T = 1;
    WHILE @T <= 2
    BEGIN
        INSERT INTO dbo.RoomTypes (HomestayId, CategoryId, Name, MaxGuests, AreaM2, Description, IsActive)
        SELECT h.Id, r.Id, CASE WHEN @T = 1 THEN N'Phòng đôi mẫu' ELSE N'Phòng gia đình mẫu' END,
               CASE WHEN @T = 1 THEN 2 ELSE 4 END, CASE WHEN @T = 1 THEN 25 ELSE 45 END,
               N'Loại phòng giả dùng trình diễn', 1
        FROM #Homestays AS h JOIN dbo.RoomCategories AS r
          ON r.Name = CASE WHEN @T = 1 THEN N'Phòng đôi' ELSE N'Phòng gia đình' END WHERE h.N = @H;
        SET @Id = CONVERT(INT, SCOPE_IDENTITY());
        INSERT INTO #Types VALUES (@H, @T, @Id);
        SET @T += 1;
    END;
    SET @H += 1;
END;
CREATE TABLE #Rooms (HomestayN INT, Slot INT, Id INT, TypeId INT, PRIMARY KEY (HomestayN, Slot));
DECLARE @R INT = 1;
WHILE @R <= 3
BEGIN
    INSERT INTO dbo.Rooms (RoomTypeId, RoomCode, IsActive, Note)
    SELECT t.Id, N'P' + CONVERT(NVARCHAR(1), t.N) + N'0' + CONVERT(NVARCHAR(1), @R), 1, N'Phòng mẫu' FROM #Types AS t;
    INSERT INTO #Rooms
    SELECT t.HomestayN, (t.N - 1) * 3 + @R, r.Id, t.Id
    FROM #Types AS t JOIN dbo.Rooms AS r ON r.RoomTypeId = t.Id
        AND r.RoomCode = N'P' + CONVERT(NVARCHAR(1), t.N) + N'0' + CONVERT(NVARCHAR(1), @R);
    SET @R += 1;
END;
-- Đơn cũ cần bảng giá lịch sử; mỗi loại có thêm cả ba giá từ hôm nay +15.
INSERT INTO dbo.RoomTypePrices (RoomTypeId, DayType, Price, EffectiveFrom, CreatedByUserId, CreatedAt)
SELECT t.Id, d.DayType, (500000 + t.HomestayN * 30000 + t.N * 70000 + d.Extra) + v.Extra,
       DATEADD(DAY, v.OffsetDays, c.Today), c.ManagerUserId, DATEADD(DAY, -90, c.NowUtc)
FROM #Types AS t CROSS JOIN #Context AS c
CROSS JOIN (VALUES ('Weekday', 0), ('Weekend', 180000), ('Holiday', 400000)) AS d(DayType, Extra)
CROSS JOIN (VALUES (-90, 0), (15, 55000)) AS v(OffsetDays, Extra);
DECLARE @PolicyId INT;
INSERT INTO dbo.CancellationPolicies (HomestayId, Scope, Name, IsActive)
SELECT Id, 'All', N'Nhà Mẫu Sen - hoàn 100% trước 10 ngày, 50% trước 5 ngày', 1 FROM #Homestays WHERE N = 1;
SET @PolicyId = CONVERT(INT, SCOPE_IDENTITY());
INSERT INTO dbo.CancellationRules (PolicyId, MinDaysBefore, RefundPercent)
VALUES (@PolicyId, 10, 100), (@PolicyId, 5, 50), (@PolicyId, 0, 0);
CREATE TABLE #Blocks (Id INT, RoomId INT, StartDate DATE, EndDate DATE);
INSERT INTO dbo.RoomBlocks (RoomId, StartDate, EndDate, Reason, Note, CreatedByUserId, CreatedAt)
OUTPUT INSERTED.Id, INSERTED.RoomId, INSERTED.StartDate, INSERTED.EndDate INTO #Blocks
SELECT r.Id, DATEADD(DAY, 20, c.Today), DATEADD(DAY, 23, c.Today),
       CASE WHEN r.HomestayN = 1 THEN 'OtherChannel' ELSE 'Maintenance' END,
       N'Khóa phòng mẫu, đã gọi chủ xác nhận', c.ManagerUserId, DATEADD(DAY, -1, c.NowUtc)
FROM #Rooms AS r CROSS JOIN #Context AS c WHERE r.HomestayN IN (1, 4) AND r.Slot = 1;
INSERT INTO dbo.RoomNights (RoomId, [Date], RoomBlockId)
SELECT b.RoomId, DATEADD(DAY, n.N, b.StartDate), b.Id FROM #Blocks AS b
CROSS JOIN (VALUES (0), (1), (2)) AS n(N);
GO
CREATE TABLE #Customers (N INT PRIMARY KEY, Id INT);
DECLARE @N INT = 1, @Id INT;
WHILE @N <= 15
BEGIN
    INSERT INTO dbo.Customers (FullName, Phone, Email, IdNumber, Note, CreatedByEmployeeId, CreatedAt)
    SELECT CHOOSE(@N, N'Nguyễn Ánh Mẫu', N'Trần Bảo Mẫu', N'Lê Cẩm Mẫu', N'Phạm Diễm Mẫu', N'Vũ Đạt Mẫu',
           N'Đỗ Giang Mẫu', N'Bùi Hiền Mẫu', N'Hoàng Khánh Mẫu', N'Đặng Lâm Mẫu', N'Ngô Minh Mẫu',
           N'Dương Ngân Mẫu', N'Đinh Oanh Mẫu', N'Hồ Phúc Mẫu', N'Mai Quỳnh Mẫu', N'Tạ Tú Mẫu'),
           '+849000003' + RIGHT('00' + CONVERT(VARCHAR(2), @N), 2),
           N'khach' + CONVERT(NVARCHAR(2), @N) + N'@example.invalid',
           CASE WHEN @N = 1 THEN '000000000001' END, N'DỮ LIỆU GIẢ - không liên hệ ngoài ứng dụng',
           c.ManagerId, DATEADD(DAY, -70, c.NowUtc) FROM #Context AS c;
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO #Customers VALUES (@N, @Id);
    SET @N += 1;
END;
GO
CREATE TABLE #Plan
(
    N INT PRIMARY KEY, Status VARCHAR(20) COLLATE DATABASE_DEFAULT, HomestayN INT, Slot INT, CheckIn DATE, CheckOut DATE,
    CreatedAt DATETIME2(3), HoldExpiresAt DATETIME2(3), SubmittedAt DATETIME2(3), DepositedAt DATETIME2(3),
    ConfirmedAt DATETIME2(3), CheckedInAt DATETIME2(3), CompletedAt DATETIME2(3), EndAt DATETIME2(3),
    Code VARCHAR(20) COLLATE DATABASE_DEFAULT, BookingId INT
);
INSERT INTO #Plan (N, Status, HomestayN, Slot, CheckIn, CheckOut, CreatedAt)
SELECT v.N, v.Status, v.H, v.Slot, DATEADD(DAY, v.Arrival, c.Today), DATEADD(DAY, v.Departure, c.Today),
       DATEADD(MINUTE, -v.AgeMinutes, c.NowUtc)
FROM #Context AS c CROSS JOIN (VALUES
    (1, 'AwaitingPayment', 1, 4, 7, 9, 11), (2, 'AwaitingPayment', 2, 4, 28, 30, 2),
    (3, 'PaymentSubmitted', 3, 4, 5, 7, 20), (4, 'PaymentSubmitted', 4, 4, 8, 10, 60),
    (5, 'Deposited', 1, 5, 3, 5, 90), (6, 'Deposited', 2, 5, 6, 8, 10),
    (7, 'Confirmed', 3, 5, 0, 2, 0), (8, 'Confirmed', 4, 5, 0, 3, 0),
    (9, 'Confirmed', 1, 6, 10, 12, 0),
    (10, 'CheckedIn', 2, 6, -2, 0, 0), (11, 'CheckedIn', 3, 6, -1, 2, 0),
    (15, 'Expired', 4, 1, 1, 3, 120), (16, 'Expired', 4, 2, 2, 4, 180),
    (17, 'Rejected', 4, 3, 4, 6, 1440), (18, 'Rejected', 4, 6, 12, 14, 2880)
) AS v(N, Status, H, Slot, Arrival, Departure, AgeMinutes);
-- Ba cơ sở, cùng kỳ đã qua hạn chờ; mỗi cơ sở có Completed, Cancelled, NoShow.
INSERT INTO #Plan (N, Status, HomestayN, Slot, CheckIn, CheckOut, CreatedAt)
SELECT v.N, v.Status, v.H, v.Slot, DATEADD(DAY, -4, c.PeriodEnd),
       DATEADD(DAY, v.DepartureOffset, c.PeriodEnd),
       DATEADD(HOUR, -6, CONVERT(DATETIME2(3), DATEADD(DAY, -12, c.PeriodEnd)))
FROM #Context AS c CROSS JOIN (VALUES
    (12, 'Completed', 1, 1, -2), (13, 'Completed', 2, 1, -2), (14, 'Completed', 3, 1, -2),
    (19, 'Cancelled', 1, 2, -1), (20, 'Cancelled', 2, 2, -1), (21, 'Cancelled', 3, 2, -1),
    (22, 'NoShow', 1, 3, 0), (23, 'NoShow', 2, 3, 0), (24, 'NoShow', 3, 3, 0)
) AS v(N, Status, H, Slot, DepartureOffset);
UPDATE #Plan SET CreatedAt = DATEADD(HOUR, -6, CONVERT(DATETIME2(3), DATEADD(DAY, -3, CheckIn)))
WHERE N IN (7, 8, 10, 11);
UPDATE p SET CreatedAt = DATEADD(DAY, -1, c.NowUtc)
FROM #Plan AS p CROSS JOIN #Context AS c WHERE p.N = 9;
UPDATE #Plan SET HoldExpiresAt = DATEADD(MINUTE, 15, CreatedAt),
    SubmittedAt = CASE WHEN Status NOT IN ('AwaitingPayment', 'Expired') THEN DATEADD(MINUTE, 2, CreatedAt) END,
    DepositedAt = CASE WHEN Status NOT IN ('AwaitingPayment', 'Expired', 'PaymentSubmitted') THEN DATEADD(MINUTE, 4, CreatedAt) END,
    ConfirmedAt = CASE WHEN Status IN ('Confirmed', 'CheckedIn', 'Completed', 'NoShow', 'Cancelled') THEN DATEADD(MINUTE, 10, CreatedAt) END,
    CheckedInAt = CASE WHEN Status IN ('CheckedIn', 'Completed') THEN DATEADD(HOUR, 7, CONVERT(DATETIME2(3), CheckIn)) END,
    CompletedAt = CASE WHEN Status = 'Completed' THEN DATEADD(HOUR, 5, CONVERT(DATETIME2(3), CheckOut)) END,
    EndAt = CASE WHEN Status = 'Expired' THEN DATEADD(MINUTE, 16, CreatedAt)
                 WHEN Status = 'Rejected' THEN DATEADD(MINUTE, 10, CreatedAt)
                 WHEN Status = 'Cancelled' THEN DATEADD(HOUR, -6, CONVERT(DATETIME2(3), DATEADD(DAY, -5, CheckIn)))
                 WHEN Status = 'NoShow' THEN DATEADD(HOUR, 1, CONVERT(DATETIME2(3), DATEADD(DAY, 1, CheckIn))) END;
-- Theo yêu cầu M1.2, snapshot HoldExpiresAt luôn giữ CreatedAt +15, kể cả đơn hết hạn do hai phiếu bị từ chối.
-- Ca này minh họa gửi lại trong hạn ban đầu, không minh họa gia hạn sau từ chối lần đầu của 7.2/9.2.
UPDATE #Plan SET SubmittedAt = DATEADD(MINUTE, 5, CreatedAt), EndAt = DATEADD(MINUTE, 6, CreatedAt) WHERE N = 16;
-- Mã theo ngày tạo tại Việt Nam, lấy tiếp bộ đếm hiện có thay vì giả định bắt đầu từ 1.
CREATE TABLE #BookingCodes (N INT PRIMARY KEY, CounterKey VARCHAR(30) COLLATE DATABASE_DEFAULT, Seq INT);
INSERT INTO #BookingCodes
SELECT p.N, k.CounterKey, ISNULL(c.LastValue, 0) + CONVERT(INT, ROW_NUMBER() OVER (PARTITION BY k.CounterKey ORDER BY p.N))
FROM #Plan AS p
CROSS APPLY (SELECT 'FJ' + CONVERT(CHAR(6), DATEADD(HOUR, 7, p.CreatedAt), 12)) AS k(CounterKey)
LEFT JOIN dbo.Counters AS c ON c.CounterKey = k.CounterKey;
IF EXISTS (SELECT 1 FROM #BookingCodes WHERE Seq > 999)
    THROW 51012, N'Bộ đếm mã đơn trong ngày vượt 999.', 1;
UPDATE p SET Code = k.CounterKey + '-' + RIGHT('000' + CONVERT(VARCHAR(3), k.Seq), 3)
FROM #Plan AS p JOIN #BookingCodes AS k ON k.N = p.N;
CREATE TABLE #NightPrices (N INT, [Date] DATE, DayType VARCHAR(10) COLLATE DATABASE_DEFAULT,
    Price DECIMAL(18,0), PRIMARY KEY (N, [Date]));
INSERT INTO #NightPrices
SELECT p.N, d.StayDate, dt.DayType, price.Price
FROM #Plan AS p JOIN #Rooms AS r ON r.HomestayN = p.HomestayN AND r.Slot = p.Slot
CROSS JOIN (VALUES (0), (1), (2), (3)) AS n(N)
CROSS APPLY (SELECT DATEADD(DAY, n.N, p.CheckIn)) AS d(StayDate)
CROSS APPLY (SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Holidays AS h WHERE h.[Date] = d.StayDate) THEN 'Holiday'
    WHEN ((DATEDIFF(DAY, CONVERT(DATE, '19000101'), d.StayDate) % 7 + 7) % 7) IN (4, 5) THEN 'Weekend'
    ELSE 'Weekday' END) AS dt(DayType)
CROSS APPLY (SELECT TOP (1) rp.Price FROM dbo.RoomTypePrices AS rp
    WHERE rp.RoomTypeId = r.TypeId AND rp.DayType = dt.DayType AND rp.EffectiveFrom <= d.StayDate
    ORDER BY rp.EffectiveFrom DESC) AS price
WHERE d.StayDate < p.CheckOut;
IF EXISTS (SELECT 1 FROM #Plan AS p WHERE (SELECT COUNT(*) FROM #NightPrices AS n WHERE n.N = p.N) <> DATEDIFF(DAY, p.CheckIn, p.CheckOut))
    THROW 51013, N'Thiếu giá cho đêm ở của dữ liệu mẫu.', 1;
DECLARE @N INT = 1, @Id INT;
WHILE @N <= 24
BEGIN
    INSERT INTO dbo.Bookings (Code, CustomerId, ContactName, ContactPhone, SourceChannel, CreatedByEmployeeId,
        AssignedEmployeeId, HomestayId, RoomTypeId, RoomId, CheckIn, CheckOut, Guests, SpecialRequests,
        Status, HoldExpiresAt, ReceiptRejectCount, PaymentSubmittedAt, DepositedAt, ConfirmDeadlineAt,
        ConfirmedAt, CheckedInAt, CompletedAt, CancelledAt, CancelReason, TotalAmount, DepositRate,
        DepositAmount, VoucherId, DiscountAmount, CommissionRate, CancellationPolicyId, RefundAmount,
        ChangeFeeAmount, CreatedAt)
    SELECT p.Code, cu.Id, cu.FullName, cu.Phone, CHOOSE((p.N - 1) % 4 + 1, 'Phone', 'Zalo', 'Messenger', 'WalkIn'),
           h.StaffId, CASE WHEN p.N NOT IN (1, 3, 5) THEN h.StaffId END, h.Id, r.TypeId, r.Id,
           p.CheckIn, p.CheckOut, 2, N'Đơn giả phục vụ trình diễn số ' + CONVERT(NVARCHAR(2), p.N),
           p.Status, p.HoldExpiresAt, CASE WHEN p.N = 16 THEN 2 ELSE 0 END, p.SubmittedAt, p.DepositedAt,
           DATEADD(MINUTE, 30, p.DepositedAt), p.ConfirmedAt, p.CheckedInAt, p.CompletedAt,
           CASE WHEN p.Status = 'Cancelled' THEN p.EndAt END,
           CASE WHEN p.Status = 'Cancelled' THEN N'Khách mẫu hủy qua nhân viên trước nhận phòng 5 ngày'
                WHEN p.Status = 'Rejected' THEN N'Chủ mẫu báo không nhận khách, đã gọi điện xác nhận' END,
           money.Total, 0.30, ROUND(money.Total * CONVERT(DECIMAL(5,4), 0.30) / 1000, 0) * 1000,
           NULL, 0, ct.CommissionRate, policy.Id, CASE WHEN p.Status = 'NoShow' THEN 0 END, 0, p.CreatedAt
    FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN
    JOIN #Rooms AS r ON r.HomestayN = p.HomestayN AND r.Slot = p.Slot
    JOIN #Customers AS cm ON cm.N = (p.N - 1) % 15 + 1 JOIN dbo.Customers AS cu ON cu.Id = cm.Id
    JOIN dbo.Contracts AS ct ON ct.HomestayId = h.Id AND ct.Status = 'Active'
        AND ct.StartDate <= CONVERT(DATE, DATEADD(HOUR, 7, p.CreatedAt)) AND ct.EndDate >= p.CheckOut
    CROSS APPLY (SELECT SUM(Price) FROM #NightPrices WHERE N = p.N) AS money(Total)
    CROSS APPLY (SELECT TOP (1) cp.Id FROM dbo.CancellationPolicies AS cp
        WHERE cp.IsActive = 1 AND (cp.HomestayId = h.Id OR cp.HomestayId IS NULL)
          AND (cp.Scope = 'All' OR EXISTS (SELECT 1 FROM #NightPrices AS n WHERE n.N = p.N AND n.DayType = 'Holiday'))
        ORDER BY CASE WHEN cp.HomestayId = h.Id AND cp.Scope = 'Holiday' THEN 1
                      WHEN cp.HomestayId = h.Id AND cp.Scope = 'All' THEN 2
                      WHEN cp.HomestayId IS NULL AND cp.Scope = 'Holiday' THEN 3 ELSE 4 END) AS policy
    WHERE p.N = @N;
    IF @@ROWCOUNT <> 1 THROW 51014, N'Không tạo được đơn mẫu: kiểm tra hợp đồng/chính sách.', 1;
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    UPDATE #Plan SET BookingId = @Id WHERE N = @N;
    SET @N += 1;
END;
INSERT INTO dbo.BookingNights (BookingId, [Date], DayType, Price, CreatedAt)
SELECT p.BookingId, n.[Date], n.DayType, n.Price, p.CreatedAt FROM #Plan AS p JOIN #NightPrices AS n ON n.N = p.N;
-- Tính lại tổng từ chính snapshot BookingNights, không nhập tay bất kỳ số tiền đơn nào.
UPDATE b SET TotalAmount = n.Total, DepositAmount = ROUND(n.Total * b.DepositRate / 1000, 0) * 1000
FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
CROSS APPLY (SELECT SUM(Price) FROM dbo.BookingNights WHERE BookingId = b.Id) AS n(Total);
UPDATE b SET RefundAmount = CASE WHEN b.Status = 'Rejected' THEN b.DepositAmount - b.DiscountAmount
    ELSE ROUND((b.DepositAmount - b.DiscountAmount) * CONVERT(DECIMAL(5,2), refundRule.RefundPercent) / 100 / 1000, 0) * 1000 END
FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
CROSS APPLY (SELECT TOP (1) cr.RefundPercent FROM dbo.CancellationRules AS cr
    WHERE cr.PolicyId = b.CancellationPolicyId
      AND cr.MinDaysBefore <= DATEDIFF(DAY, CONVERT(DATE, DATEADD(HOUR, 7, p.EndAt)), b.CheckIn)
    ORDER BY cr.MinDaysBefore DESC) AS refundRule
WHERE b.Status IN ('Rejected', 'Cancelled');
INSERT INTO dbo.RoomNights (RoomId, [Date], BookingId, CreatedAt)
SELECT b.RoomId, n.[Date], b.Id, b.CreatedAt
FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id JOIN dbo.BookingNights AS n ON n.BookingId = b.Id
WHERE b.Status IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn', 'Completed');
UPDATE c SET LastValue = v.MaxSeq, UpdatedAt = x.NowUtc
FROM dbo.Counters AS c JOIN (SELECT CounterKey, MAX(Seq) AS MaxSeq FROM #BookingCodes GROUP BY CounterKey) AS v
    ON v.CounterKey = c.CounterKey CROSS JOIN #Context AS x;
INSERT INTO dbo.Counters (CounterKey, LastValue)
SELECT CounterKey, MAX(Seq) FROM #BookingCodes AS k
WHERE NOT EXISTS (SELECT 1 FROM dbo.Counters AS c WHERE c.CounterKey = k.CounterKey) GROUP BY CounterKey;
GO
CREATE TABLE #Events (N INT, Seq INT, ToStatus VARCHAR(20) COLLATE DATABASE_DEFAULT,
    AtUtc DATETIME2(3), ActorType VARCHAR(10) COLLATE DATABASE_DEFAULT,
    ActorUserId INT NULL, OnBehalf BIT, Note NVARCHAR(500) COLLATE DATABASE_DEFAULT, PRIMARY KEY (N, Seq));
INSERT INTO #Events
SELECT p.N, 1, 'AwaitingPayment', p.CreatedAt, 'Employee', h.StaffUserId, 0, N'Tạo đơn mẫu'
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN;
INSERT INTO #Events
SELECT p.N, 2, 'PaymentSubmitted', p.SubmittedAt, 'Employee', h.StaffUserId, 0, N'Khách báo đã chuyển cọc mẫu'
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN WHERE p.SubmittedAt IS NOT NULL AND p.N <> 16;
INSERT INTO #Events
SELECT p.N, 3, 'Deposited', p.DepositedAt, 'Employee', e.UserId, 0, N'Kế toán xác minh đủ cọc mẫu'
FROM #Plan AS p CROSS JOIN #Employees AS e WHERE p.DepositedAt IS NOT NULL AND e.Code = 'NV0006';
INSERT INTO #Events
SELECT p.N, 4, 'Confirmed', p.ConfirmedAt, 'Employee', h.StaffUserId, 1,
    N'Đã gọi chủ lúc ' + CONVERT(NVARCHAR(5), DATEADD(HOUR, 7, p.ConfirmedAt), 108) + N', chủ đồng ý nhận khách'
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN WHERE p.ConfirmedAt IS NOT NULL;
INSERT INTO #Events
SELECT p.N, 5, 'CheckedIn', p.CheckedInAt, 'Employee', h.StaffUserId, 1,
    N'Đã gọi chủ lúc ' + CONVERT(NVARCHAR(5), DATEADD(HOUR, 7, p.CheckedInAt), 108) + N', chủ báo khách đã nhận phòng'
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN WHERE p.CheckedInAt IS NOT NULL;
INSERT INTO #Events
SELECT p.N, 6, 'Completed', p.CompletedAt, 'Employee', h.StaffUserId, 1,
    N'Đã gọi chủ lúc ' + CONVERT(NVARCHAR(5), DATEADD(HOUR, 7, p.CompletedAt), 108) + N', chủ báo khách đã trả phòng'
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN WHERE p.CompletedAt IS NOT NULL;
INSERT INTO #Events
SELECT p.N, 7, p.Status, p.EndAt, CASE WHEN p.Status = 'Expired' THEN 'System' ELSE 'Employee' END,
    CASE WHEN p.Status = 'Cancelled' THEN c.ManagerUserId WHEN p.Status <> 'Expired' THEN h.StaffUserId END,
    CASE WHEN p.Status IN ('Rejected', 'NoShow') THEN 1 ELSE 0 END,
    CASE WHEN p.Status = 'Expired' THEN N'Hết thời gian giữ phòng'
         WHEN p.Status = 'Cancelled' THEN N'Quản lý duyệt hủy, giữ mức hoàn đã báo khách trước 5 ngày'
         ELSE N'Đã gọi chủ lúc ' + CONVERT(NVARCHAR(5), DATEADD(HOUR, 7, p.EndAt), 108)
              + CASE WHEN p.Status = 'Rejected' THEN N', chủ từ chối nhận khách' ELSE N', chủ báo khách không đến' END END
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN CROSS JOIN #Context AS c
WHERE p.EndAt IS NOT NULL AND p.N <> 16;
INSERT INTO #Events
SELECT p.N, v.Seq, v.ToStatus, DATEADD(MINUTE, v.OffsetMinutes, p.CreatedAt), 'Employee',
    CASE WHEN v.Seq IN (3, 5) THEN e.UserId ELSE h.StaffUserId END, 0, v.Note
FROM #Plan AS p JOIN #Homestays AS h ON h.N = p.HomestayN CROSS JOIN #Employees AS e
CROSS JOIN (VALUES (2, 'PaymentSubmitted', 1, N'Ghi nhận chứng từ mẫu lần 1'),
    (3, 'AwaitingPayment', 2, N'Kế toán từ chối chứng từ lần 1, khách gửi lại trong hạn giữ ban đầu'),
    (4, 'PaymentSubmitted', 5, N'Khách gửi lại chứng từ mẫu lần 2'),
    (5, 'Expired', 6, N'Kế toán từ chối lần 2, hết số lần cho phép')) AS v(Seq, ToStatus, OffsetMinutes, Note)
WHERE p.N = 16 AND e.Code = 'NV0006';
INSERT INTO dbo.BookingStatusHistories (BookingId, FromStatus, ToStatus, ActorType, ActorUserId, OnBehalfOfOwner, Note, ChangedAt, CreatedAt)
SELECT p.BookingId, LAG(e.ToStatus) OVER (PARTITION BY e.N ORDER BY e.Seq), e.ToStatus,
       e.ActorType, e.ActorUserId, e.OnBehalf, e.Note, e.AtUtc, e.AtUtc
FROM #Events AS e JOIN #Plan AS p ON p.N = e.N;
GO
CREATE TABLE #PaymentPlan
(
    N INT IDENTITY(1,1) PRIMARY KEY, BookingId INT, Direction VARCHAR(3) COLLATE DATABASE_DEFAULT,
    Type VARCHAR(20) COLLATE DATABASE_DEFAULT, Method VARCHAR(20) COLLATE DATABASE_DEFAULT,
    Amount DECIMAL(18,0), Status VARCHAR(20) COLLATE DATABASE_DEFAULT, RecordedBy INT,
    RecordedAt DATETIME2(3), VerifiedBy INT, VerifiedAt DATETIME2(3), BankRef VARCHAR(100) COLLATE DATABASE_DEFAULT,
    RejectReason NVARCHAR(500) COLLATE DATABASE_DEFAULT, VoucherNo VARCHAR(20) COLLATE DATABASE_DEFAULT, Id INT
);
INSERT INTO #PaymentPlan (BookingId, Direction, Type, Method, Amount, Status, RecordedBy, RecordedAt, VerifiedBy, VerifiedAt)
SELECT b.Id, 'In', 'Deposit', CASE WHEN p.N % 2 = 0 OR p.N = 5 THEN 'BankTransfer' ELSE 'Cash' END,
       b.DepositAmount - b.DiscountAmount,
       CASE WHEN b.Status = 'PaymentSubmitted' THEN 'Pending' ELSE 'Verified' END,
       h.StaffUserId, p.SubmittedAt,
       CASE WHEN b.Status <> 'PaymentSubmitted' THEN e.UserId END, p.DepositedAt
FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId
JOIN #Homestays AS h ON h.N = p.HomestayN CROSS JOIN #Employees AS e
WHERE p.SubmittedAt IS NOT NULL AND p.N <> 16 AND e.Code = 'NV0006';
INSERT INTO #PaymentPlan (BookingId, Direction, Type, Method, Amount, Status, RecordedBy, RecordedAt, VerifiedBy, VerifiedAt, RejectReason)
SELECT p.BookingId, 'In', 'Deposit', 'BankTransfer', b.DepositAmount, 'Rejected', h.StaffUserId,
       DATEADD(MINUTE, v.RecordMinute, p.CreatedAt), e.UserId, DATEADD(MINUTE, v.RejectMinute, p.CreatedAt),
       N'Chứng từ minh họa không hợp lệ, không ghi thu thực tế'
FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId JOIN #Homestays AS h ON h.N = p.HomestayN
CROSS JOIN #Employees AS e CROSS JOIN (VALUES (1, 2), (5, 6)) AS v(RecordMinute, RejectMinute)
WHERE p.N = 16 AND e.Code = 'NV0006';
INSERT INTO #PaymentPlan (BookingId, Direction, Type, Method, Amount, Status, RecordedBy, RecordedAt,
    VerifiedBy, VerifiedAt, BankRef)
SELECT b.Id, 'Out', 'Refund', 'BankTransfer', b.RefundAmount,
       CASE WHEN p.N IN (17, 19) THEN 'Pending' ELSE 'Verified' END,
       c.ManagerUserId, p.EndAt,
       CASE WHEN p.N NOT IN (17, 19) THEN e.UserId END,
       CASE WHEN p.N NOT IN (17, 19) THEN DATEADD(MINUTE, 30, p.EndAt) END,
       CASE WHEN p.N NOT IN (17, 19) THEN 'SEED-REFUND-' + CONVERT(VARCHAR(2), p.N) END
FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId CROSS JOIN #Context AS c
CROSS JOIN #Employees AS e WHERE b.RefundAmount > 0 AND e.Code = 'NV0007';
CREATE TABLE #PaymentCodes (N INT PRIMARY KEY, CounterKey VARCHAR(30) COLLATE DATABASE_DEFAULT, Seq INT);
INSERT INTO #PaymentCodes
SELECT p.N, k.CounterKey, ISNULL(c.LastValue, 0) + CONVERT(INT, ROW_NUMBER() OVER (PARTITION BY k.CounterKey ORDER BY p.RecordedAt, p.N))
FROM #PaymentPlan AS p
CROSS APPLY (SELECT CASE WHEN p.Direction = 'In' THEN 'PT' ELSE 'PC' END
    + LEFT(CONVERT(CHAR(6), DATEADD(HOUR, 7, p.RecordedAt), 12), 4)) AS k(CounterKey)
LEFT JOIN dbo.Counters AS c ON c.CounterKey = k.CounterKey;
IF EXISTS (SELECT 1 FROM #PaymentCodes WHERE Seq > 9999)
    THROW 51015, N'Bộ đếm số phiếu trong tháng vượt 9999.', 1;
UPDATE p SET VoucherNo = k.CounterKey + '-' + RIGHT('0000' + CONVERT(VARCHAR(4), k.Seq), 4)
FROM #PaymentPlan AS p JOIN #PaymentCodes AS k ON k.N = p.N;
DECLARE @N INT = 1, @Id INT;
WHILE @N <= (SELECT MAX(N) FROM #PaymentPlan)
BEGIN
    INSERT INTO dbo.Payments (VoucherNo, BookingId, Direction, Type, Method, Amount, TransferredAt, PayerOrPayeeName,
        PayerOrPayeeAccount, TransferContent, BankRef, Status, RecordedByUserId, RecordedAt,
        VerifiedByUserId, VerifiedAt, RejectReason, Note, CreatedAt)
    SELECT p.VoucherNo, p.BookingId, p.Direction, p.Type, p.Method, p.Amount,
           CASE WHEN p.Direction = 'In' THEN CASE WHEN p.Method = 'Cash' THEN p.RecordedAt ELSE DATEADD(MINUTE, -2, p.RecordedAt) END END, b.ContactName,
           CASE WHEN p.Method = 'BankTransfer' THEN '000000000000' END,
           N'DU LIEU GIA ' + REPLACE(b.Code, '-', ''), p.BankRef, p.Status, p.RecordedBy, p.RecordedAt,
           p.VerifiedBy, p.VerifiedAt, p.RejectReason, N'Phiếu mẫu, không có giao dịch tiền thật', p.RecordedAt
    FROM #PaymentPlan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId WHERE p.N = @N;
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    UPDATE #PaymentPlan SET Id = @Id WHERE N = @N;
    SET @N += 1;
END;
UPDATE c SET LastValue = v.MaxSeq, UpdatedAt = x.NowUtc
FROM dbo.Counters AS c JOIN (SELECT CounterKey, MAX(Seq) AS MaxSeq FROM #PaymentCodes GROUP BY CounterKey) AS v
    ON v.CounterKey = c.CounterKey CROSS JOIN #Context AS x;
INSERT INTO dbo.Counters (CounterKey, LastValue)
SELECT CounterKey, MAX(Seq) FROM #PaymentCodes AS k
WHERE NOT EXISTS (SELECT 1 FROM dbo.Counters AS c WHERE c.CounterKey = k.CounterKey) GROUP BY CounterKey;
-- Hủy sau cọc đi qua yêu cầu và giữ mức hoàn ở thời điểm gửi, trước lúc duyệt một phút.
INSERT INTO dbo.BookingChangeRequests (BookingId, RequestType, Reason, QuotedRefundAmount, Status,
    RequestedByUserId, RequestedAt, DecidedByUserId, DecidedAt, DecisionNote, CreatedAt)
SELECT b.Id, 'Cancel', N'Khách mẫu đề nghị hủy trước 5 ngày', b.RefundAmount, 'Applied', h.StaffUserId,
       DATEADD(MINUTE, -1, p.EndAt), c.ManagerUserId, p.EndAt, N'Đồng ý hủy theo chính sách đã báo khách',
       DATEADD(MINUTE, -1, p.EndAt)
FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId JOIN #Homestays AS h ON h.N = p.HomestayN
CROSS JOIN #Context AS c WHERE b.Status = 'Cancelled';
INSERT INTO dbo.BookingChangeRequests (BookingId, RequestType, Reason, QuotedRefundAmount, Status,
    RequestedByUserId, RequestedAt, CreatedAt)
SELECT b.Id, 'Cancel', N'Khách mẫu muốn hủy, đang chờ Quản lý',
       ROUND((b.DepositAmount - b.DiscountAmount) * CONVERT(DECIMAL(5,2), refundRule.RefundPercent) / 100 / 1000, 0) * 1000,
       'Pending', h.StaffUserId, c.NowUtc, c.NowUtc
FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId JOIN #Homestays AS h ON h.N = p.HomestayN
CROSS JOIN #Context AS c
CROSS APPLY (SELECT TOP (1) cr.RefundPercent FROM dbo.CancellationRules AS cr
    WHERE cr.PolicyId = b.CancellationPolicyId AND cr.MinDaysBefore <= DATEDIFF(DAY, c.Today, b.CheckIn)
    ORDER BY cr.MinDaysBefore DESC) AS refundRule WHERE p.N = 9;
GO
CREATE TABLE #Bank (N INT PRIMARY KEY, Id INT, PaymentId INT NULL);
DECLARE @N INT = 1, @Id INT, @PaymentId INT;
WHILE @N <= 6
BEGIN
    SET @PaymentId = NULL;
    IF @N IN (1, 2)
        SELECT @PaymentId = pay.Id FROM #Plan AS p JOIN #PaymentPlan AS pay ON pay.BookingId = p.BookingId
        WHERE p.N = @N + 4 AND pay.Type = 'Deposit' AND pay.Status = 'Verified';
    INSERT INTO dbo.BankTransactions (Source, AccountNumber, BankReference, Amount, TransferContent, SenderName,
        SenderAccount, TransactionTime, MatchStatus, MatchedBookingId, ProcessedByUserId, ProcessedAt,
        Note, CreatedByUserId, CreatedAt)
    SELECT 'Manual', '000000000000', 'SEED-BANK-' + CONVERT(VARCHAR(1), @N),
           CASE WHEN @N IN (1, 2) THEN b.DepositAmount - b.DiscountAmount
                WHEN @N = 3 THEN b.DepositAmount - b.DiscountAmount - 1000
                WHEN @N = 4 THEN b.DepositAmount - b.DiscountAmount ELSE 100000 END,
           CASE WHEN @N <= 4 THEN N'DU LIEU GIA ' + REPLACE(b.Code, '-', '') ELSE N'Giao dịch giả không có mã đơn' END,
           N'NGUOI GUI MAU', '000000000001',
           CASE WHEN @N IN (1, 2) THEN DATEADD(SECOND, -30, b.PaymentSubmittedAt)
                WHEN @N = 3 THEN DATEADD(MINUTE, -1, c.NowUtc)
                WHEN @N = 4 THEN DATEADD(MINUTE, 1, p.EndAt) ELSE DATEADD(HOUR, -1, c.NowUtc) END,
           CHOOSE(@N, 'Matched', 'Matched', 'Underpaid', 'LatePayment', 'Unmatched', 'Ignored'),
           CASE WHEN @N <= 4 THEN b.Id END,
           CASE WHEN @N IN (1, 2, 6) THEN e.UserId END,
           CASE WHEN @N IN (1, 2) THEN b.DepositedAt WHEN @N = 6 THEN c.NowUtc END,
           CASE WHEN @N = 3 THEN N'Khách mẫu chuyển thiếu 1.000đ, chưa ghi phiếu thu'
                WHEN @N = 4 THEN N'Tiền mẫu đến sau khi hết hạn giữ phòng, chờ xử lý'
                WHEN @N = 6 THEN N'Bỏ qua dòng thử nghiệm không liên quan đặt phòng'
                ELSE N'Dòng sao kê giả để trình diễn' END, e.UserId,
           CASE WHEN @N IN (1, 2) THEN b.DepositedAt ELSE c.NowUtc END
    FROM #Plan AS p JOIN dbo.Bookings AS b ON b.Id = p.BookingId CROSS JOIN #Context AS c
    CROSS JOIN #Employees AS e
    WHERE p.N = CASE WHEN @N <= 2 THEN @N + 4 WHEN @N = 3 THEN 1 ELSE 15 END AND e.Code = 'NV0006';
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO #Bank VALUES (@N, @Id, @PaymentId);
    SET @N += 1;
END;
UPDATE p SET BankTransactionId = b.Id FROM dbo.Payments AS p JOIN #Bank AS b ON b.PaymentId = p.Id;
GO
-- 7.6.6 đầy đủ: phần trăm hoàn làm tròn trước, phần giữ/hoa hồng lấy MIN, tổng bằng tổng dòng.
CREATE TABLE #SettlementLines
(
    HomestayId INT, BookingId INT, ItemType VARCHAR(20) COLLATE DATABASE_DEFAULT,
    RoomAmount DECIMAL(18,0), DepositAmount DECIMAL(18,0),
    RefundAmount DECIMAL(18,0), RetainedAmount DECIMAL(18,0), CommissionAmount DECIMAL(18,0),
    DiscountAmount DECIMAL(18,0), PayoutAmount DECIMAL(18,0), CommissionRate DECIMAL(5,4)
);
INSERT INTO #SettlementLines
SELECT b.HomestayId, b.Id,
       CASE WHEN b.Status = 'Cancelled' THEN 'CancelledRetained' ELSE b.Status END,
       b.TotalAmount, b.DepositAmount + b.ChangeFeeAmount, ISNULL(b.RefundAmount, 0),
       base.Retained + b.ChangeFeeAmount, comm.BaseCommission + comm.FeeCommission,
       CASE WHEN disc.RetainedDiscount > 0 THEN disc.RetainedDiscount ELSE 0 END,
       base.Retained + b.ChangeFeeAmount - comm.BaseCommission - comm.FeeCommission, b.CommissionRate
FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id CROSS JOIN #Context AS c
CROSS APPLY (SELECT CASE WHEN b.DepositAmount - b.DiscountAmount > 0
    THEN ROUND(100.0 * ISNULL(b.RefundAmount, 0) / (b.DepositAmount - b.DiscountAmount), 0) ELSE 0 END) AS refund(PercentRefunded)
CROSS APPLY (SELECT b.DepositAmount - ROUND(b.DepositAmount * refund.PercentRefunded / 100 / 1000, 0) * 1000) AS base(Retained)
CROSS APPLY (SELECT CASE WHEN ROUND(b.TotalAmount * b.CommissionRate, 0) < base.Retained
    THEN ROUND(b.TotalAmount * b.CommissionRate, 0) ELSE base.Retained END,
    CASE WHEN ROUND(b.ChangeFeeAmount * b.CommissionRate, 0) < b.ChangeFeeAmount
    THEN ROUND(b.ChangeFeeAmount * b.CommissionRate, 0) ELSE b.ChangeFeeAmount END) AS comm(BaseCommission, FeeCommission)
CROSS APPLY (SELECT b.DiscountAmount - ROUND(b.DiscountAmount * refund.PercentRefunded / 100 / 1000, 0) * 1000) AS disc(RetainedDiscount)
WHERE b.Status IN ('Completed', 'NoShow', 'Cancelled') AND b.ReplacedByBookingId IS NULL
  AND b.CheckOut BETWEEN CASE WHEN DAY(c.PeriodEnd) = 15 THEN DATEFROMPARTS(YEAR(c.PeriodEnd), MONTH(c.PeriodEnd), 1)
                             ELSE DATEFROMPARTS(YEAR(c.PeriodEnd), MONTH(c.PeriodEnd), 16) END AND c.PeriodEnd
  AND base.Retained + b.ChangeFeeAmount > 0;
CREATE TABLE #Settlements (N INT PRIMARY KEY, Id INT, HomestayId INT);
DECLARE @N INT = 1, @Id INT;
WHILE @N <= 3
BEGIN
    INSERT INTO dbo.Settlements (HomestayId, [Year], [Month], PeriodNo, TotalRoomAmount, TotalRetained,
        TotalCommission, TotalDiscount, PayoutAmount, Status, SentAt, SentByUserId, PaidAt, PaidByUserId,
        PaidBankRef, OwnerFeedback, CreatedAt)
    SELECT h.Id, YEAR(c.PeriodEnd), MONTH(c.PeriodEnd), CASE WHEN DAY(c.PeriodEnd) = 15 THEN 1 ELSE 2 END,
           SUM(l.RoomAmount), SUM(l.RetainedAmount), SUM(l.CommissionAmount), SUM(l.DiscountAmount), SUM(l.PayoutAmount),
           CHOOSE(@N, 'Draft', 'Sent', 'Paid'),
           CASE WHEN @N >= 2 THEN DATEADD(MINUTE, -2, c.NowUtc) END,
           CASE WHEN @N >= 2 THEN e.UserId END,
           CASE WHEN @N = 3 THEN DATEADD(MINUTE, -1, c.NowUtc) END,
           CASE WHEN @N = 3 THEN e.UserId END,
           CASE WHEN @N = 3 THEN 'SEED-SETTLEMENT-PAID' END,
           CASE WHEN @N >= 2 THEN N'Đã gọi chủ mẫu, gửi bản in đối soát để kiểm tra' END,
           DATEADD(MINUTE, -3, c.NowUtc)
    FROM #Homestays AS h JOIN #SettlementLines AS l ON l.HomestayId = h.Id CROSS JOIN #Context AS c
    CROSS JOIN #Employees AS e WHERE h.N = @N AND e.Code = 'NV0007'
    GROUP BY h.Id, c.PeriodEnd, c.NowUtc, e.UserId;
    SET @Id = CONVERT(INT, SCOPE_IDENTITY());
    INSERT INTO #Settlements SELECT @N, @Id, Id FROM #Homestays WHERE N = @N;
    SET @N += 1;
END;
INSERT INTO dbo.SettlementItems (SettlementId, BookingId, ItemType, RoomAmount, DepositAmount, RefundAmount,
    RetainedAmount, CommissionAmount, DiscountAmount, PayoutAmount, CommissionRate, CreatedAt)
SELECT s.Id, l.BookingId, l.ItemType, l.RoomAmount, l.DepositAmount, l.RefundAmount, l.RetainedAmount,
       l.CommissionAmount, l.DiscountAmount, l.PayoutAmount, l.CommissionRate, DATEADD(MINUTE, -3, c.NowUtc)
FROM #SettlementLines AS l JOIN #Settlements AS s ON s.HomestayId = l.HomestayId CROSS JOIN #Context AS c;
GO
INSERT INTO dbo.WorkTasks (Title, Description, AssigneeEmployeeId, CreatedByUserId, BookingId, HomestayId,
    Priority, DueAt, Status, CompletedAt, ResultNote, CreatedAt)
SELECT v.Title, N'Việc giao minh họa, thông tin và thời gian đều là dữ liệu mẫu', e.Id, c.ManagerUserId,
       p.BookingId, h.Id, v.Priority, DATEADD(HOUR, v.DueHours, c.NowUtc), v.Status,
       CASE WHEN v.Status = 'Done' THEN DATEADD(HOUR, -1, c.NowUtc) END,
       CASE WHEN v.Status = 'Done' THEN N'Đã hoàn thành việc mẫu' END, DATEADD(DAY, -2, c.NowUtc)
FROM #Context AS c CROSS JOIN (VALUES
    (N'Gọi chủ xác nhận đơn quá hạn', 'NV0002', 5, 1, 1, -1, 'Open'),
    (N'Chuẩn bị đón khách hôm nay', 'NV0003', 7, 3, 1, 2, 'Open'),
    (N'Kiểm tra chứng từ chờ xác minh', 'NV0006', 3, 3, 2, 3, 'Open'),
    (N'In đối soát gửi chủ mẫu', 'NV0007', 13, 2, 2, 24, 'Open'),
    (N'Kiểm tra giấy tờ cơ sở mẫu', 'NV0002', 8, 4, 3, -2, 'Done')
) AS v(Title, Code, BookingN, HomestayN, Priority, DueHours, Status)
JOIN #Employees AS e ON e.Code = v.Code JOIN #Plan AS p ON p.N = v.BookingN JOIN #Homestays AS h ON h.N = v.HomestayN;
INSERT INTO dbo.Notifications (UserId, Message, RefType, RefId, IsRead, CreatedAt)
SELECT e.UserId, N'Thông báo mẫu: có công việc cần kiểm tra trên Homestay Desktop.', 'Employee',
       CONVERT(VARCHAR(30), e.Id), 0, c.NowUtc FROM #Employees AS e CROSS JOIN #Context AS c;
INSERT INTO dbo.AuditLogs (UserId, ActionCode, TableName, RecordId, NewValues, Note, MachineName, CreatedAt)
SELECT ev.ActorUserId,
       CASE ev.ToStatus WHEN 'AwaitingPayment' THEN CASE WHEN ev.Seq = 1 THEN 'Booking.Create' ELSE 'Payment.Reject' END
            WHEN 'PaymentSubmitted' THEN 'Payment.Record' WHEN 'Deposited' THEN 'Payment.Verify'
            WHEN 'Confirmed' THEN 'Booking.Confirm' WHEN 'CheckedIn' THEN 'Booking.CheckIn'
            WHEN 'Completed' THEN 'Booking.Complete' WHEN 'Rejected' THEN 'Booking.Reject'
            WHEN 'Cancelled' THEN 'Booking.Cancel' WHEN 'NoShow' THEN 'Booking.NoShow'
            WHEN 'Expired' THEN CASE WHEN ev.ActorType = 'System' THEN 'Booking.Expire' ELSE 'Payment.Reject' END END,
       'Bookings', CONVERT(VARCHAR(50), p.BookingId),
       (SELECT ev.ToStatus AS Status, ev.OnBehalf AS OnBehalfOfOwner FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
       ev.Note, N'SEED', ev.AtUtc FROM #Events AS ev JOIN #Plan AS p ON p.N = ev.N;
INSERT INTO dbo.AuditLogs (UserId, ActionCode, TableName, RecordId, Note, MachineName, CreatedAt)
SELECT pay.VerifiedByUserId, 'Payment.Verify', 'Payments', CONVERT(VARCHAR(50), pay.Id),
       N'Kế toán xác minh phiếu mẫu', N'SEED', pay.VerifiedAt
FROM dbo.Payments AS pay JOIN #PaymentPlan AS pp ON pp.Id = pay.Id WHERE pay.Status = 'Verified';
INSERT INTO dbo.AuditLogs (UserId, ActionCode, TableName, RecordId, Note, MachineName, CreatedAt)
SELECT s.CreatedBy, s.ActionCode, 'Settlements', CONVERT(VARCHAR(50), s.Id), N'Đối soát mẫu', N'SEED', s.AtUtc
FROM (SELECT st.Id, e.UserId AS CreatedBy, 'Settlement.Create' AS ActionCode, st.CreatedAt AS AtUtc
      FROM dbo.Settlements AS st JOIN #Settlements AS ids ON ids.Id = st.Id CROSS JOIN #Employees AS e WHERE e.Code = 'NV0007'
      UNION ALL
      SELECT st.Id, st.SentByUserId, 'Settlement.Send', st.SentAt
      FROM dbo.Settlements AS st JOIN #Settlements AS ids ON ids.Id = st.Id WHERE st.SentAt IS NOT NULL
      UNION ALL
      SELECT st.Id, st.PaidByUserId, 'Settlement.Pay', st.PaidAt
      FROM dbo.Settlements AS st JOIN #Settlements AS ids ON ids.Id = st.Id WHERE st.PaidAt IS NOT NULL) AS s;
GO
-- Kiểm tra dữ liệu ngay trong transaction; sai thì không ghi Version 20.
IF (SELECT COUNT(*) FROM #Plan) <> 24 OR EXISTS
    (SELECT Status FROM #Plan GROUP BY Status HAVING COUNT(*) < 2)
    THROW 51020, N'Không đủ đơn theo trạng thái.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id CROSS JOIN #Context AS c
    WHERE b.CheckIn < DATEADD(DAY, -45, c.Today) OR b.CheckOut > DATEADD(DAY, 30, c.Today)
       OR b.CreatedAt >= c.NowUtc OR b.HoldExpiresAt <> DATEADD(MINUTE, 15, b.CreatedAt)
       OR b.VoucherId IS NOT NULL OR b.DiscountAmount <> 0 OR b.ChangeFeeAmount <> 0
       OR (b.PaymentSubmittedAt IS NOT NULL AND (b.PaymentSubmittedAt <= b.CreatedAt OR b.PaymentSubmittedAt > b.HoldExpiresAt))
       OR (b.DepositedAt IS NOT NULL AND (b.DepositedAt <= b.PaymentSubmittedAt OR b.ConfirmDeadlineAt <> DATEADD(MINUTE, 30, b.DepositedAt)))
       OR (b.ConfirmedAt IS NOT NULL AND (b.ConfirmedAt <= b.DepositedAt OR b.ConfirmedAt > b.ConfirmDeadlineAt))
       OR (b.CheckedInAt IS NOT NULL AND (b.CheckedInAt <= b.ConfirmedAt OR b.CheckedInAt > c.NowUtc))
       OR (b.CompletedAt IS NOT NULL AND (b.CompletedAt <= b.CheckedInAt OR b.CompletedAt > c.NowUtc))
       OR (b.CancelledAt IS NOT NULL AND (b.CancelledAt <= b.ConfirmedAt OR b.CancelledAt > c.NowUtc))
)
    THROW 51021, N'Mốc thời gian hoặc trường giai đoạn 8 không hợp lệ.', 1;
IF (SELECT COUNT(*) FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
    CROSS JOIN #Context AS c WHERE b.Status = 'Confirmed' AND b.CheckIn = c.Today) <> 2
 OR (SELECT COUNT(*) FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
    CROSS JOIN #Context AS c WHERE b.Status = 'CheckedIn' AND b.CheckOut = c.Today) <> 1
 OR (SELECT COUNT(*) FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id WHERE b.AssignedEmployeeId IS NULL) <> 3
 OR NOT EXISTS (SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id CROSS JOIN #Context AS c
    WHERE b.Status = 'Deposited' AND b.ConfirmDeadlineAt < c.NowUtc)
 OR NOT EXISTS (SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id CROSS JOIN #Context AS c
    WHERE b.Status = 'AwaitingPayment' AND b.HoldExpiresAt > c.NowUtc AND b.HoldExpiresAt < DATEADD(MINUTE, 5, c.NowUtc))
    THROW 51022, N'Thiếu tình huống vận hành tương đối theo ngày chạy.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
    CROSS APPLY (SELECT SUM(n.Price) AS Total, COUNT(*) AS Nights FROM dbo.BookingNights AS n WHERE n.BookingId = b.Id) AS x
    WHERE x.Total <> b.TotalAmount OR x.Nights <> DATEDIFF(DAY, b.CheckIn, b.CheckOut)
       OR b.DepositAmount <> ROUND(x.Total * b.DepositRate / 1000, 0) * 1000
)
    THROW 51023, N'Tổng tiền hoặc số đêm không khớp snapshot.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
    CROSS APPLY (SELECT COUNT(*) AS Nights FROM dbo.RoomNights AS n WHERE n.BookingId = b.Id) AS x
    WHERE x.Nights <> CASE WHEN b.Status IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn', 'Completed')
                          THEN DATEDIFF(DAY, b.CheckIn, b.CheckOut) ELSE 0 END
)
    THROW 51024, N'Sổ chiếm phòng không khớp trạng thái.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id
    OUTER APPLY (SELECT TOP (1) h.ToStatus FROM dbo.BookingStatusHistories AS h WHERE h.BookingId = b.Id ORDER BY h.ChangedAt DESC, h.Id DESC) AS lastStatus
    WHERE lastStatus.ToStatus IS NULL OR lastStatus.ToStatus <> b.Status
       OR b.ReceiptRejectCount <> (SELECT COUNT(*) FROM dbo.Payments AS pay WHERE pay.BookingId = b.Id AND pay.Type = 'Deposit' AND pay.Status = 'Rejected')
       OR (b.DepositedAt IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Payments AS pay WHERE pay.BookingId = b.Id AND pay.Type = 'Deposit' AND pay.Status = 'Verified' AND pay.Amount = b.DepositAmount - b.DiscountAmount))
       OR (b.Status = 'PaymentSubmitted' AND NOT EXISTS (SELECT 1 FROM dbo.Payments AS pay WHERE pay.BookingId = b.Id AND pay.Type = 'Deposit' AND pay.Status = 'Pending'))
       OR (b.RefundAmount > 0 AND NOT EXISTS (SELECT 1 FROM dbo.Payments AS pay WHERE pay.BookingId = b.Id AND pay.Type = 'Refund' AND pay.Amount = b.RefundAmount AND pay.Status IN ('Pending', 'Verified')))
)
    THROW 51025, N'Lịch sử hoặc phiếu thu chi không khớp đơn.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Payments AS pay JOIN #PaymentPlan AS pp ON pp.Id = pay.Id
    LEFT JOIN dbo.Employees AS e ON e.UserId = pay.VerifiedByUserId
    WHERE (pay.Status = 'Verified' AND (e.Position <> 'Accountant' OR e.Id IS NULL OR pay.RecordedByUserId = pay.VerifiedByUserId OR pay.VerifiedAt <= pay.RecordedAt))
       OR (pay.Direction = 'Out' AND pay.Status = 'Verified' AND pay.BankRef IS NULL)
)
    THROW 51026, N'Phiếu mẫu vi phạm tách nhiệm vụ hoặc thời gian xác minh.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.Settlements AS s JOIN #Settlements AS ids ON ids.Id = s.Id CROSS JOIN #Context AS c
    CROSS APPLY (SELECT SUM(i.RoomAmount), SUM(i.RetainedAmount), SUM(i.CommissionAmount), SUM(i.DiscountAmount), SUM(i.PayoutAmount)
                 FROM dbo.SettlementItems AS i WHERE i.SettlementId = s.Id) AS x(RoomAmount, Retained, Commission, Discount, Payout)
    WHERE s.TotalRoomAmount <> x.RoomAmount OR s.TotalRetained <> x.Retained OR s.TotalCommission <> x.Commission
       OR s.TotalDiscount <> x.Discount OR s.PayoutAmount <> x.Payout
       OR DATEADD(DAY, 2, c.PeriodEnd) >= c.Today
       OR (s.Status IN ('Sent', 'Paid') AND EXISTS
          (SELECT 1 FROM dbo.Bookings AS b WHERE b.HomestayId = s.HomestayId
           AND YEAR(b.CheckOut) = s.[Year] AND MONTH(b.CheckOut) = s.[Month]
           AND CASE WHEN DAY(b.CheckOut) <= 15 THEN 1 ELSE 2 END = s.PeriodNo
           AND b.Status IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn')))
)
    THROW 51027, N'Đối soát sai tổng/kỳ hoặc còn đơn chưa chốt.', 1;
IF EXISTS
(
    SELECT 1 FROM dbo.BankTransactions AS b JOIN #Bank AS ids ON ids.Id = b.Id
    LEFT JOIN dbo.Payments AS pay ON pay.BankTransactionId = b.Id
    WHERE b.MatchStatus = 'Matched' AND (pay.Id IS NULL OR pay.Status <> 'Verified' OR pay.BookingId <> b.MatchedBookingId)
)
    THROW 51028, N'Dòng sao kê đã khớp không có phiếu xác minh tương ứng.', 1;
GO
DROP PROCEDURE #SeedPassword20;
GO
INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (20, N'020_seed_demo.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
-- Báo cáo sau khi commit, không đưa mật khẩu/hash ra kết quả.
SELECT TableName, TotalRows FROM
(
    SELECT 'Users' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Users
UNION ALL
    SELECT 'Employees' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Employees
UNION ALL
    SELECT 'EmployeeAreas' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.EmployeeAreas
UNION ALL
    SELECT 'Owners' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Owners
UNION ALL
    SELECT 'UserSessions' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.UserSessions
UNION ALL
    SELECT 'Areas' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Areas
UNION ALL
    SELECT 'Amenities' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Amenities
UNION ALL
    SELECT 'RoomCategories' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.RoomCategories
UNION ALL
    SELECT 'Holidays' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Holidays
UNION ALL
    SELECT 'SystemSettings' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.SystemSettings
UNION ALL
    SELECT 'Counters' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Counters
UNION ALL
    SELECT 'SchemaVersions' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.SchemaVersions
UNION ALL
    SELECT 'Homestays' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Homestays
UNION ALL
    SELECT 'HomestayAmenities' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.HomestayAmenities
UNION ALL
    SELECT 'HomestayImages' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.HomestayImages
UNION ALL
    SELECT 'VerificationDocuments' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.VerificationDocuments
UNION ALL
    SELECT 'Contracts' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Contracts
UNION ALL
    SELECT 'CancellationPolicies' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.CancellationPolicies
UNION ALL
    SELECT 'CancellationRules' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.CancellationRules
UNION ALL
    SELECT 'RoomTypes' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.RoomTypes
UNION ALL
    SELECT 'Rooms' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Rooms
UNION ALL
    SELECT 'RoomTypePrices' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.RoomTypePrices
UNION ALL
    SELECT 'RoomBlocks' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.RoomBlocks
UNION ALL
    SELECT 'RoomNights' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.RoomNights
UNION ALL
    SELECT 'Customers' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Customers
UNION ALL
    SELECT 'Bookings' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Bookings
UNION ALL
    SELECT 'BookingNights' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.BookingNights
UNION ALL
    SELECT 'BookingStatusHistories' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.BookingStatusHistories
UNION ALL
    SELECT 'BookingChangeRequests' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.BookingChangeRequests
UNION ALL
    SELECT 'Vouchers' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Vouchers
UNION ALL
    SELECT 'Payments' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Payments
UNION ALL
    SELECT 'BankTransactions' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.BankTransactions
UNION ALL
    SELECT 'Settlements' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Settlements
UNION ALL
    SELECT 'SettlementItems' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.SettlementItems
UNION ALL
    SELECT 'Notifications' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.Notifications
UNION ALL
    SELECT 'WorkTasks' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.WorkTasks
UNION ALL
    SELECT 'AuditLogs' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.AuditLogs
UNION ALL
    SELECT 'StoredFiles' AS TableName, COUNT_BIG(*) AS TotalRows FROM dbo.StoredFiles
) AS counts ORDER BY TableName;
SELECT b.Status, COUNT(*) AS BookingCount FROM dbo.Bookings AS b JOIN #Plan AS p ON p.BookingId = b.Id GROUP BY b.Status ORDER BY b.Status;
SELECT e.EmployeeCode, e.FullName, e.Position, u.Status, u.MustChangePassword
FROM dbo.Employees AS e JOIN dbo.Users AS u ON u.Id = e.UserId JOIN #Employees AS ids ON ids.Id = e.Id ORDER BY e.EmployeeCode;
SELECT c.CounterKey, c.LastValue FROM dbo.Counters AS c
WHERE c.CounterKey IN (SELECT CounterKey FROM #BookingCodes UNION SELECT CounterKey FROM #PaymentCodes) ORDER BY c.CounterKey;
GO
DROP TABLE #Context, #Employees, #Owners, #Homestays, #Images, #Documents, #Types, #Rooms, #Blocks,
    #Customers, #Plan, #BookingCodes, #NightPrices, #Events, #PaymentPlan, #PaymentCodes, #Bank, #SettlementLines, #Settlements;
GO
SET NOEXEC OFF;
GO
