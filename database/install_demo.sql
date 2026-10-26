-- tệp sinh tự động, không sửa tay
:on error exit
USE [master];
GO
IF DB_ID(N'$(DbName)') IS NOT NULL
BEGIN
    ALTER DATABASE [$(DbName)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$(DbName)];
END;
GO
CREATE DATABASE [$(DbName)] COLLATE Vietnamese_CI_AS;
GO
ALTER DATABASE [$(DbName)] SET READ_COMMITTED_SNAPSHOT ON;
GO
USE [$(DbName)];
GO

-- 001_schema.sql
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
IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SchemaVersions
    (
        [Version] INT NOT NULL,
        ScriptName NVARCHAR(200) NOT NULL,
        AppliedAt DATETIME2(3) NOT NULL,
        CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SchemaVersions_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_SchemaVersions PRIMARY KEY ([Version])
    );
END;
GO
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 1)
    SET NOEXEC ON;
GO
BEGIN TRANSACTION;
GO

CREATE TABLE dbo.Users
(
    Id INT IDENTITY(1,1) NOT NULL,
    Username VARCHAR(50) NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    Status VARCHAR(20) NOT NULL,
    MustChangePassword BIT NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT 1,
    FailedLoginCount INT NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT 0,
    LockoutEndUtc DATETIME2(3) NULL,
    LockReason NVARCHAR(500) NULL,
    SecurityStamp UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT NEWID(),
    PasswordChangedAt DATETIME2(3) NULL,
    LastLoginAt DATETIME2(3) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Users PRIMARY KEY (Id),
    CONSTRAINT UQ_Users_Username UNIQUE (Username),
    CONSTRAINT CK_Users_Status CHECK (Status IN ('Active', 'Locked', 'Disabled')),
    CONSTRAINT CK_Users_FailedLoginCount CHECK (FailedLoginCount >= 0),
    CONSTRAINT CK_Users_LockReason CHECK (Status <> 'Locked' OR (LockReason IS NOT NULL AND LEN(LTRIM(RTRIM(LockReason))) > 0))
);
GO

CREATE TABLE dbo.Employees
(
    Id INT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    EmployeeCode VARCHAR(20) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Phone VARCHAR(16) NOT NULL,
    Email NVARCHAR(256) NULL,
    Position VARCHAR(20) NOT NULL,
    EmploymentStatus VARCHAR(20) NOT NULL,
    HasAllAreas BIT NOT NULL CONSTRAINT DF_Employees_HasAllAreas DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Employees_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Employees PRIMARY KEY (Id),
    CONSTRAINT UQ_Employees_UserId UNIQUE (UserId),
    CONSTRAINT UQ_Employees_EmployeeCode UNIQUE (EmployeeCode),
    CONSTRAINT UQ_Employees_Phone UNIQUE (Phone),
    CONSTRAINT CK_Employees_Position CHECK (Position IN ('Staff', 'Accountant', 'Manager')),
    CONSTRAINT CK_Employees_EmploymentStatus CHECK (EmploymentStatus IN ('Active', 'Suspended', 'Resigned'))
);
GO

CREATE TABLE dbo.EmployeeAreas
(
    EmployeeId INT NOT NULL,
    AreaId INT NOT NULL,
    AssignedByEmployeeId INT NOT NULL,
    AssignedAt DATETIME2(3) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_EmployeeAreas_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_EmployeeAreas PRIMARY KEY (EmployeeId, AreaId)
);
GO

CREATE TABLE dbo.Owners
(
    Id INT IDENTITY(1,1) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Phone VARCHAR(16) NOT NULL,
    Email NVARCHAR(256) NULL,
    Address NVARCHAR(300) NULL,
    IdNumber VARCHAR(20) NULL,
    BankName NVARCHAR(100) NULL,
    BankAccountNo VARCHAR(30) NULL,
    BankAccountName NVARCHAR(100) NULL,
    Status VARCHAR(20) NOT NULL,
    ApprovedByEmployeeId INT NULL,
    ApprovedAt DATETIME2(3) NULL,
    Note NVARCHAR(1000) NULL,
    CreatedByEmployeeId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Owners_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Owners PRIMARY KEY (Id),
    CONSTRAINT UQ_Owners_Phone UNIQUE (Phone),
    CONSTRAINT CK_Owners_Status CHECK (Status IN ('Pending', 'Approved', 'Suspended', 'Rejected')),
    CONSTRAINT CK_Owners_Approval CHECK (Status <> 'Approved' OR (ApprovedByEmployeeId IS NOT NULL AND ApprovedAt IS NOT NULL)),
    CONSTRAINT CK_Owners_ApprovedBankDetails CHECK (Status <> 'Approved' OR (BankName IS NOT NULL AND LEN(LTRIM(RTRIM(BankName))) > 0 AND BankAccountNo IS NOT NULL AND LEN(LTRIM(RTRIM(BankAccountNo))) > 0 AND BankAccountName IS NOT NULL AND LEN(LTRIM(RTRIM(BankAccountName))) > 0))
);
GO

CREATE UNIQUE INDEX UX_Owners_IdNumber
    ON dbo.Owners (IdNumber)
    WHERE IdNumber IS NOT NULL;
GO

CREATE TABLE dbo.UserSessions
(
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    MachineName NVARCHAR(100) NOT NULL,
    AppVersion VARCHAR(20) NOT NULL,
    LoginAt DATETIME2(3) NOT NULL,
    LastSeenAt DATETIME2(3) NOT NULL,
    EndedAt DATETIME2(3) NULL,
    EndReason VARCHAR(20) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_UserSessions_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_UserSessions PRIMARY KEY (Id),
    CONSTRAINT CK_UserSessions_EndReason CHECK (EndReason IN ('Logout', 'Idle', 'Replaced', 'ForcedByManager', 'AccountChanged')),
    CONSTRAINT CK_UserSessions_EndState CHECK ((EndedAt IS NULL AND EndReason IS NULL) OR (EndedAt IS NOT NULL AND EndReason IS NOT NULL))
);
GO

CREATE UNIQUE INDEX UX_UserSessions_OneActiveSession
    ON dbo.UserSessions (UserId)
    WHERE EndedAt IS NULL;
GO

CREATE TABLE dbo.Areas
(
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    Province NVARCHAR(100) NOT NULL,
    AreaGroup VARCHAR(20) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Areas_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Areas PRIMARY KEY (Id),
    CONSTRAINT UQ_Areas_Province_Name UNIQUE (Province, Name),
    CONSTRAINT CK_Areas_AreaGroup CHECK (AreaGroup IN ('InnerCity', 'Suburb', 'OtherProvince'))
);
GO

CREATE TABLE dbo.Amenities
(
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Amenities_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Amenities PRIMARY KEY (Id),
    CONSTRAINT UQ_Amenities_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.RoomCategories
(
    Id INT IDENTITY(1,1) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoomCategories_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_RoomCategories PRIMARY KEY (Id),
    CONSTRAINT UQ_RoomCategories_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.Holidays
(
    [Date] DATE NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Holidays_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Holidays PRIMARY KEY ([Date])
);
GO

CREATE TABLE dbo.SystemSettings
(
    SettingKey VARCHAR(100) NOT NULL,
    SettingValue NVARCHAR(500) NOT NULL,
    Description NVARCHAR(500) NULL,
    UpdatedByUserId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SystemSettings_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_SystemSettings PRIMARY KEY (SettingKey)
);
GO

CREATE TABLE dbo.Counters
(
    CounterKey VARCHAR(30) NOT NULL,
    LastValue INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Counters_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Counters PRIMARY KEY (CounterKey),
    CONSTRAINT CK_Counters_LastValue CHECK (LastValue >= 0)
);
GO

CREATE TABLE dbo.Homestays
(
    Id INT IDENTITY(1,1) NOT NULL,
    OwnerId INT NOT NULL,
    AreaId INT NOT NULL,
    Name NVARCHAR(200) NOT NULL,
    Address NVARCHAR(300) NOT NULL,
    ContactPhone VARCHAR(16) NULL,
    Description NVARCHAR(4000) NULL,
    CheckinGuide NVARCHAR(2000) NULL,
    CheckinTime TIME(0) NOT NULL CONSTRAINT DF_Homestays_CheckinTime DEFAULT '14:00',
    CheckoutTime TIME(0) NOT NULL CONSTRAINT DF_Homestays_CheckoutTime DEFAULT '12:00',
    Status VARCHAR(20) NOT NULL,
    StatusReason NVARCHAR(500) NULL,
    SubmittedAt DATETIME2(3) NULL,
    ApprovedByEmployeeId INT NULL,
    ApprovedAt DATETIME2(3) NULL,
    CreatedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Homestays_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Homestays PRIMARY KEY (Id),
    CONSTRAINT UQ_Homestays_OwnerId_Name UNIQUE (OwnerId, Name),
    CONSTRAINT CK_Homestays_Status CHECK (Status IN ('Draft', 'PendingReview', 'Active', 'Suspended', 'Ended'))
);
GO

CREATE TABLE dbo.HomestayAmenities
(
    HomestayId INT NOT NULL,
    AmenityId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_HomestayAmenities_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_HomestayAmenities PRIMARY KEY (HomestayId, AmenityId)
);
GO

CREATE TABLE dbo.HomestayImages
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NOT NULL,
    RoomTypeId INT NULL,
    FileId UNIQUEIDENTIFIER NOT NULL,
    Caption NVARCHAR(200) NULL,
    SortOrder INT NOT NULL CONSTRAINT DF_HomestayImages_SortOrder DEFAULT 0,
    IsCover BIT NOT NULL CONSTRAINT DF_HomestayImages_IsCover DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_HomestayImages_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_HomestayImages PRIMARY KEY (Id)
);
GO

CREATE UNIQUE INDEX UX_HomestayImages_OneCover
    ON dbo.HomestayImages (HomestayId)
    WHERE IsCover = 1;
GO

CREATE TABLE dbo.VerificationDocuments
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NOT NULL,
    DocumentType VARCHAR(30) NOT NULL,
    FileId UNIQUEIDENTIFIER NOT NULL,
    ExpiryDate DATE NULL,
    UploadedByUserId INT NOT NULL,
    IsSuperseded BIT NOT NULL CONSTRAINT DF_VerificationDocuments_IsSuperseded DEFAULT 0,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_VerificationDocuments_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_VerificationDocuments PRIMARY KEY (Id),
    CONSTRAINT CK_VerificationDocuments_DocumentType CHECK (DocumentType IN ('BusinessLicense', 'FireSafety', 'PropertyPhoto', 'Other'))
);
GO

CREATE TABLE dbo.Contracts
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NOT NULL,
    ContractNo VARCHAR(30) NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    CommissionRate DECIMAL(5,4) NOT NULL,
    Status VARCHAR(20) NOT NULL,
    SignedByEmployeeId INT NOT NULL,
    TerminatedAt DATETIME2(3) NULL,
    TerminateReason NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Contracts_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Contracts PRIMARY KEY (Id),
    CONSTRAINT UQ_Contracts_ContractNo UNIQUE (ContractNo),
    CONSTRAINT CK_Contracts_Dates CHECK (EndDate > StartDate),
    CONSTRAINT CK_Contracts_CommissionRate CHECK (CommissionRate > 0 AND CommissionRate < 1),
    CONSTRAINT CK_Contracts_Status CHECK (Status IN ('Active', 'Expired', 'Terminated')),
    CONSTRAINT CK_Contracts_Termination CHECK (Status <> 'Terminated' OR (TerminatedAt IS NOT NULL AND TerminateReason IS NOT NULL AND LEN(LTRIM(RTRIM(TerminateReason))) > 0))
);
GO

CREATE UNIQUE INDEX UX_Contracts_OneActive
    ON dbo.Contracts (HomestayId)
    WHERE Status = 'Active';
GO

CREATE TABLE dbo.CancellationPolicies
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NULL,
    Scope VARCHAR(10) NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_CancellationPolicies_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_CancellationPolicies PRIMARY KEY (Id),
    CONSTRAINT CK_CancellationPolicies_Scope CHECK (Scope IN ('All', 'Holiday'))
);
GO

CREATE UNIQUE INDEX UX_CancellationPolicies_OneActiveScope
    ON dbo.CancellationPolicies (HomestayId, Scope)
    WHERE IsActive = 1;
GO

CREATE TABLE dbo.CancellationRules
(
    Id INT IDENTITY(1,1) NOT NULL,
    PolicyId INT NOT NULL,
    MinDaysBefore INT NOT NULL,
    RefundPercent INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_CancellationRules_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_CancellationRules PRIMARY KEY (Id),
    CONSTRAINT UQ_CancellationRules_PolicyId_MinDaysBefore UNIQUE (PolicyId, MinDaysBefore),
    CONSTRAINT CK_CancellationRules_MinDaysBefore CHECK (MinDaysBefore >= 0),
    CONSTRAINT CK_CancellationRules_RefundPercent CHECK (RefundPercent BETWEEN 0 AND 100)
);
GO

CREATE TABLE dbo.RoomTypes
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NOT NULL,
    CategoryId INT NOT NULL,
    Name NVARCHAR(100) NOT NULL,
    MaxGuests INT NOT NULL,
    AreaM2 INT NULL,
    Description NVARCHAR(1000) NULL,
    IsActive BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoomTypes_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_RoomTypes PRIMARY KEY (Id),
    CONSTRAINT UQ_RoomTypes_HomestayId_Name UNIQUE (HomestayId, Name),
    CONSTRAINT CK_RoomTypes_MaxGuests CHECK (MaxGuests > 0),
    CONSTRAINT CK_RoomTypes_AreaM2 CHECK (AreaM2 IS NULL OR AreaM2 > 0)
);
GO

CREATE TABLE dbo.Rooms
(
    Id INT IDENTITY(1,1) NOT NULL,
    RoomTypeId INT NOT NULL,
    RoomCode NVARCHAR(20) NOT NULL,
    IsActive BIT NOT NULL,
    Note NVARCHAR(255) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Rooms_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Rooms PRIMARY KEY (Id),
    CONSTRAINT UQ_Rooms_RoomTypeId_RoomCode UNIQUE (RoomTypeId, RoomCode)
);
GO

CREATE TABLE dbo.RoomTypePrices
(
    Id INT IDENTITY(1,1) NOT NULL,
    RoomTypeId INT NOT NULL,
    DayType VARCHAR(10) NOT NULL,
    Price DECIMAL(18,0) NOT NULL,
    EffectiveFrom DATE NOT NULL,
    CreatedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoomTypePrices_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_RoomTypePrices PRIMARY KEY (Id),
    CONSTRAINT UQ_RoomTypePrices_RoomTypeId_DayType_EffectiveFrom UNIQUE (RoomTypeId, DayType, EffectiveFrom),
    CONSTRAINT CK_RoomTypePrices_DayType CHECK (DayType IN ('Weekday', 'Weekend', 'Holiday')),
    CONSTRAINT CK_RoomTypePrices_Price CHECK (Price > 0)
);
GO

CREATE TABLE dbo.RoomBlocks
(
    Id INT IDENTITY(1,1) NOT NULL,
    RoomId INT NOT NULL,
    StartDate DATE NOT NULL,
    EndDate DATE NOT NULL,
    Reason VARCHAR(20) NOT NULL,
    Note NVARCHAR(255) NULL,
    CreatedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoomBlocks_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_RoomBlocks PRIMARY KEY (Id),
    CONSTRAINT CK_RoomBlocks_Dates CHECK (EndDate > StartDate),
    CONSTRAINT CK_RoomBlocks_Reason CHECK (Reason IN ('OtherChannel', 'Maintenance'))
);
GO

CREATE TABLE dbo.RoomNights
(
    RoomId INT NOT NULL,
    [Date] DATE NOT NULL,
    BookingId INT NULL,
    RoomBlockId INT NULL,
    ChangeRequestId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_RoomNights_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_RoomNights PRIMARY KEY (RoomId, [Date]),
    CONSTRAINT CK_RoomNights_OneSource CHECK ((CASE WHEN BookingId IS NULL THEN 0 ELSE 1 END + CASE WHEN RoomBlockId IS NULL THEN 0 ELSE 1 END + CASE WHEN ChangeRequestId IS NULL THEN 0 ELSE 1 END) = 1)
);
GO

CREATE TABLE dbo.Customers
(
    Id INT IDENTITY(1,1) NOT NULL,
    FullName NVARCHAR(100) NOT NULL,
    Phone VARCHAR(16) NOT NULL,
    Email NVARCHAR(256) NULL,
    IdNumber VARCHAR(20) NULL,
    Note NVARCHAR(1000) NULL,
    CreatedByEmployeeId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Customers PRIMARY KEY (Id),
    CONSTRAINT UQ_Customers_Phone UNIQUE (Phone)
);
GO

CREATE TABLE dbo.Bookings
(
    Id INT IDENTITY(1,1) NOT NULL,
    Code VARCHAR(20) NOT NULL,
    CustomerId INT NOT NULL,
    ContactName NVARCHAR(100) NOT NULL,
    ContactPhone VARCHAR(16) NOT NULL,
    SourceChannel VARCHAR(20) NOT NULL,
    CreatedByEmployeeId INT NOT NULL,
    AssignedEmployeeId INT NULL,
    HomestayId INT NOT NULL,
    RoomTypeId INT NOT NULL,
    RoomId INT NOT NULL,
    CheckIn DATE NOT NULL,
    CheckOut DATE NOT NULL,
    Guests INT NOT NULL,
    SpecialRequests NVARCHAR(1000) NULL,
    Status VARCHAR(20) NOT NULL,
    HoldExpiresAt DATETIME2(3) NOT NULL,
    ReceiptRejectCount TINYINT NOT NULL CONSTRAINT DF_Bookings_ReceiptRejectCount DEFAULT 0,
    PaymentSubmittedAt DATETIME2(3) NULL,
    DepositedAt DATETIME2(3) NULL,
    ConfirmDeadlineAt DATETIME2(3) NULL,
    ConfirmAlertSentAt DATETIME2(3) NULL,
    ConfirmedAt DATETIME2(3) NULL,
    CheckedInAt DATETIME2(3) NULL,
    CompletedAt DATETIME2(3) NULL,
    CancelledAt DATETIME2(3) NULL,
    CancelReason NVARCHAR(500) NULL,
    TotalAmount DECIMAL(18,0) NOT NULL,
    DepositRate DECIMAL(5,4) NOT NULL,
    DepositAmount DECIMAL(18,0) NOT NULL,
    VoucherId INT NULL,
    DiscountAmount DECIMAL(18,0) NOT NULL CONSTRAINT DF_Bookings_DiscountAmount DEFAULT 0,
    CommissionRate DECIMAL(5,4) NOT NULL,
    CancellationPolicyId INT NOT NULL,
    RefundAmount DECIMAL(18,0) NULL,
    ChangeFeeAmount DECIMAL(18,0) NOT NULL CONSTRAINT DF_Bookings_ChangeFeeAmount DEFAULT 0,
    ReplacedByBookingId INT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Bookings PRIMARY KEY (Id),
    CONSTRAINT UQ_Bookings_Code UNIQUE (Code),
    CONSTRAINT CK_Bookings_SourceChannel CHECK (SourceChannel IN ('Phone', 'Zalo', 'Messenger', 'WalkIn', 'Other')),
    CONSTRAINT CK_Bookings_StayDates CHECK (CheckOut > CheckIn),
    CONSTRAINT CK_Bookings_Guests CHECK (Guests > 0),
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn', 'Completed', 'Expired', 'Rejected', 'Cancelled', 'NoShow')),
    CONSTRAINT CK_Bookings_DepositRate CHECK (DepositRate > 0 AND DepositRate <= 1),
    CONSTRAINT CK_Bookings_DepositAmount CHECK (DepositAmount > 0 AND DepositAmount <= TotalAmount),
    CONSTRAINT CK_Bookings_DiscountAmount CHECK (DiscountAmount >= 0 AND DiscountAmount < DepositAmount),
    CONSTRAINT CK_Bookings_VoucherDiscount CHECK ((VoucherId IS NULL AND DiscountAmount = 0) OR (VoucherId IS NOT NULL AND DiscountAmount > 0)),
    CONSTRAINT CK_Bookings_CommissionRate CHECK (CommissionRate > 0 AND CommissionRate < 1),
    CONSTRAINT CK_Bookings_RefundAmount CHECK (RefundAmount IS NULL OR (RefundAmount >= 0 AND RefundAmount <= DepositAmount - DiscountAmount)),
    CONSTRAINT CK_Bookings_ChangeFeeAmount CHECK (ChangeFeeAmount >= 0),
    CONSTRAINT CK_Bookings_ConfirmedAt CHECK ((Status IN ('Confirmed', 'CheckedIn', 'Completed', 'NoShow') AND ConfirmedAt IS NOT NULL) OR (Status IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Rejected', 'Expired') AND ConfirmedAt IS NULL) OR Status = 'Cancelled'),
    CONSTRAINT CK_Bookings_DepositedAt CHECK (Status NOT IN ('Deposited', 'Confirmed', 'CheckedIn', 'Completed', 'NoShow', 'Rejected') OR DepositedAt IS NOT NULL),
    CONSTRAINT CK_Bookings_CheckedInAt CHECK (Status NOT IN ('CheckedIn', 'Completed') OR CheckedInAt IS NOT NULL),
    CONSTRAINT CK_Bookings_CompletedAt CHECK (Status <> 'Completed' OR CompletedAt IS NOT NULL),
    CONSTRAINT CK_Bookings_Cancellation CHECK (Status <> 'Cancelled' OR (CancelledAt IS NOT NULL AND CancelReason IS NOT NULL AND LEN(LTRIM(RTRIM(CancelReason))) > 0)),
    CONSTRAINT CK_Bookings_RejectionReason CHECK (Status <> 'Rejected' OR (CancelReason IS NOT NULL AND LEN(LTRIM(RTRIM(CancelReason))) > 0))
);
GO

CREATE TABLE dbo.BookingNights
(
    BookingId INT NOT NULL,
    [Date] DATE NOT NULL,
    DayType VARCHAR(10) NOT NULL,
    Price DECIMAL(18,0) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BookingNights_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BookingNights PRIMARY KEY (BookingId, [Date]),
    CONSTRAINT CK_BookingNights_DayType CHECK (DayType IN ('Weekday', 'Weekend', 'Holiday')),
    CONSTRAINT CK_BookingNights_Price CHECK (Price > 0)
);
GO

CREATE TABLE dbo.BookingStatusHistories
(
    Id BIGINT IDENTITY(1,1) NOT NULL,
    BookingId INT NOT NULL,
    FromStatus VARCHAR(20) NULL,
    ToStatus VARCHAR(20) NOT NULL,
    ActorType VARCHAR(10) NOT NULL,
    ActorUserId INT NULL,
    OnBehalfOfOwner BIT NOT NULL CONSTRAINT DF_BookingStatusHistories_OnBehalfOfOwner DEFAULT 0,
    Note NVARCHAR(500) NULL,
    ChangedAt DATETIME2(3) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BookingStatusHistories_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_BookingStatusHistories PRIMARY KEY (Id),
    CONSTRAINT CK_BookingStatusHistories_FromStatus CHECK (FromStatus IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn', 'Completed', 'Expired', 'Rejected', 'Cancelled', 'NoShow')),
    CONSTRAINT CK_BookingStatusHistories_ToStatus CHECK (ToStatus IN ('AwaitingPayment', 'PaymentSubmitted', 'Deposited', 'Confirmed', 'CheckedIn', 'Completed', 'Expired', 'Rejected', 'Cancelled', 'NoShow')),
    CONSTRAINT CK_BookingStatusHistories_ActorType CHECK (ActorType IN ('Employee', 'System')),
    CONSTRAINT CK_BookingStatusHistories_Actor CHECK ((ActorType = 'System' AND ActorUserId IS NULL) OR (ActorType = 'Employee' AND ActorUserId IS NOT NULL)),
    CONSTRAINT CK_BookingStatusHistories_OnBehalfNote CHECK (OnBehalfOfOwner = 0 OR (Note IS NOT NULL AND LEN(LTRIM(RTRIM(Note))) > 0))
);
GO

CREATE TABLE dbo.BookingChangeRequests
(
    Id INT IDENTITY(1,1) NOT NULL,
    BookingId INT NOT NULL,
    RequestType VARCHAR(30) NOT NULL,
    NewCheckIn DATE NULL,
    NewCheckOut DATE NULL,
    NewRoomTypeId INT NULL,
    NewRoomId INT NULL,
    Reason NVARCHAR(500) NOT NULL,
    QuotedRefundAmount DECIMAL(18,0) NULL,
    ChangeFee DECIMAL(18,0) NULL,
    DepositCarriedOver DECIMAL(18,0) NULL,
    AdditionalDeposit DECIMAL(18,0) NULL,
    NewTotalAmount DECIMAL(18,0) NULL,
    Status VARCHAR(20) NOT NULL,
    RequestedByUserId INT NOT NULL,
    RequestedAt DATETIME2(3) NOT NULL,
    DecidedByUserId INT NULL,
    DecidedAt DATETIME2(3) NULL,
    DecisionNote NVARCHAR(500) NULL,
    PaymentDeadlineAt DATETIME2(3) NULL,
    FinanceConfirmedByUserId INT NULL,
    FinanceConfirmedAt DATETIME2(3) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BookingChangeRequests_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_BookingChangeRequests PRIMARY KEY (Id),
    CONSTRAINT CK_BookingChangeRequests_RequestType CHECK (RequestType IN ('ChangeDates', 'ChangeRoomType', 'ChangeDatesAndRoomType', 'Cancel')),
    CONSTRAINT CK_BookingChangeRequests_Fields CHECK ((RequestType = 'Cancel' AND NewCheckIn IS NULL AND NewCheckOut IS NULL AND NewRoomTypeId IS NULL AND NewRoomId IS NULL)
        OR (RequestType = 'ChangeDates' AND NewCheckIn IS NOT NULL AND NewCheckOut IS NOT NULL AND NewCheckOut > NewCheckIn AND NewRoomTypeId IS NULL)
        OR (RequestType = 'ChangeRoomType' AND NewRoomTypeId IS NOT NULL AND NewCheckIn IS NULL AND NewCheckOut IS NULL)
        OR (RequestType = 'ChangeDatesAndRoomType' AND NewCheckIn IS NOT NULL AND NewCheckOut IS NOT NULL AND NewCheckOut > NewCheckIn AND NewRoomTypeId IS NOT NULL)),
    CONSTRAINT CK_BookingChangeRequests_QuotedRefundAmount CHECK (QuotedRefundAmount IS NULL OR QuotedRefundAmount >= 0),
    CONSTRAINT CK_BookingChangeRequests_ChangeFee CHECK (ChangeFee IS NULL OR ChangeFee >= 0),
    CONSTRAINT CK_BookingChangeRequests_DepositCarriedOver CHECK (DepositCarriedOver IS NULL OR DepositCarriedOver >= 0),
    CONSTRAINT CK_BookingChangeRequests_AdditionalDeposit CHECK (AdditionalDeposit IS NULL OR AdditionalDeposit >= 0),
    CONSTRAINT CK_BookingChangeRequests_NewTotalAmount CHECK (NewTotalAmount IS NULL OR NewTotalAmount > 0),
    CONSTRAINT CK_BookingChangeRequests_Status CHECK (Status IN ('Pending', 'AwaitingPayment', 'Applied', 'Rejected', 'Expired')),
    CONSTRAINT CK_BookingChangeRequests_DecisionState CHECK ((Status = 'Pending' AND DecidedByUserId IS NULL AND DecidedAt IS NULL) OR (Status <> 'Pending' AND DecidedByUserId IS NOT NULL AND DecidedAt IS NOT NULL)),
    CONSTRAINT CK_BookingChangeRequests_DecisionNote CHECK (Status <> 'Rejected' OR (DecisionNote IS NOT NULL AND LEN(LTRIM(RTRIM(DecisionNote))) > 0)),
    CONSTRAINT CK_BookingChangeRequests_PaymentDeadline CHECK (Status <> 'AwaitingPayment' OR PaymentDeadlineAt IS NOT NULL),
    CONSTRAINT CK_BookingChangeRequests_FinanceConfirmation CHECK (Status <> 'Applied' OR AdditionalDeposit IS NULL OR AdditionalDeposit = 0 OR (FinanceConfirmedByUserId IS NOT NULL AND FinanceConfirmedAt IS NOT NULL)),
    CONSTRAINT CK_BookingChangeRequests_FinancePair CHECK ((FinanceConfirmedByUserId IS NULL AND FinanceConfirmedAt IS NULL) OR (FinanceConfirmedByUserId IS NOT NULL AND FinanceConfirmedAt IS NOT NULL)),
    CONSTRAINT CK_BookingChangeRequests_ActorSeparation CHECK ((DecidedByUserId IS NULL OR DecidedByUserId <> RequestedByUserId) AND (FinanceConfirmedByUserId IS NULL OR (FinanceConfirmedByUserId <> RequestedByUserId AND DecidedByUserId IS NOT NULL AND FinanceConfirmedByUserId <> DecidedByUserId)))
);
GO

CREATE UNIQUE INDEX UX_BookingChangeRequests_OneOpenRequest
    ON dbo.BookingChangeRequests (BookingId)
    WHERE Status IN ('Pending', 'AwaitingPayment');
GO

CREATE TABLE dbo.Vouchers
(
    Id INT IDENTITY(1,1) NOT NULL,
    Code VARCHAR(20) NOT NULL,
    CustomerId INT NOT NULL,
    Amount DECIMAL(18,0) NOT NULL,
    Balance DECIMAL(18,0) NOT NULL,
    ExpiresOn DATE NOT NULL,
    Status VARCHAR(20) NOT NULL,
    Reason NVARCHAR(500) NOT NULL,
    SourceBookingId INT NULL,
    IssuedByEmployeeId INT NOT NULL,
    IssuedAt DATETIME2(3) NOT NULL,
    RevokedAt DATETIME2(3) NULL,
    RevokeReason NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Vouchers_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Vouchers PRIMARY KEY (Id),
    CONSTRAINT UQ_Vouchers_Code UNIQUE (Code),
    CONSTRAINT CK_Vouchers_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Vouchers_Balance CHECK (Balance >= 0 AND Balance <= Amount),
    CONSTRAINT CK_Vouchers_Status CHECK (Status IN ('Active', 'UsedUp', 'Expired', 'Revoked')),
    CONSTRAINT CK_Vouchers_BalanceState CHECK ((Status = 'Active' AND Balance > 0) OR (Status = 'UsedUp' AND Balance = 0) OR Status IN ('Expired', 'Revoked')),
    CONSTRAINT CK_Vouchers_Revocation CHECK (Status <> 'Revoked' OR (RevokedAt IS NOT NULL AND RevokeReason IS NOT NULL AND LEN(LTRIM(RTRIM(RevokeReason))) > 0))
);
GO

CREATE UNIQUE INDEX UX_Vouchers_SourceBookingId
    ON dbo.Vouchers (SourceBookingId)
    WHERE SourceBookingId IS NOT NULL;
GO

CREATE TABLE dbo.Payments
(
    Id INT IDENTITY(1,1) NOT NULL,
    VoucherNo VARCHAR(20) NOT NULL,
    BookingId INT NOT NULL,
    ChangeRequestId INT NULL,
    Direction VARCHAR(3) NOT NULL,
    Type VARCHAR(20) NOT NULL,
    Method VARCHAR(20) NOT NULL,
    Amount DECIMAL(18,0) NOT NULL,
    BankTransactionId INT NULL,
    EvidenceFileId UNIQUEIDENTIFIER NULL,
    PayerOrPayeeName NVARCHAR(100) NULL,
    PayerOrPayeeAccount VARCHAR(30) NULL,
    TransferContent NVARCHAR(255) NULL,
    BankRef VARCHAR(100) NULL,
    Status VARCHAR(20) NOT NULL,
    RecordedByUserId INT NOT NULL,
    RecordedAt DATETIME2(3) NOT NULL,
    VerifiedByUserId INT NULL,
    VerifiedAt DATETIME2(3) NULL,
    RejectReason NVARCHAR(500) NULL,
    ReviewAlertSentAt DATETIME2(3) NULL,
    Note NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Payments PRIMARY KEY (Id),
    CONSTRAINT UQ_Payments_VoucherNo UNIQUE (VoucherNo),
    CONSTRAINT CK_Payments_Direction CHECK (Direction IN ('In', 'Out')),
    CONSTRAINT CK_Payments_Type CHECK (Type IN ('Deposit', 'AdditionalDeposit', 'Refund', 'Compensation')),
    CONSTRAINT CK_Payments_TypeDirection CHECK ((Type IN ('Deposit', 'AdditionalDeposit') AND Direction = 'In') OR (Type IN ('Refund', 'Compensation') AND Direction = 'Out')),
    CONSTRAINT CK_Payments_ChangeRequest CHECK ((Type = 'AdditionalDeposit' AND ChangeRequestId IS NOT NULL) OR (Type <> 'AdditionalDeposit' AND ChangeRequestId IS NULL)),
    CONSTRAINT CK_Payments_Method CHECK (Method IN ('BankTransfer', 'Cash')),
    CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Payments_Status CHECK (Status IN ('Pending', 'Verified', 'Rejected')),
    CONSTRAINT CK_Payments_Verification CHECK (Status <> 'Verified' OR (VerifiedByUserId IS NOT NULL AND VerifiedAt IS NOT NULL)),
    CONSTRAINT CK_Payments_RejectReason CHECK (Status <> 'Rejected' OR (RejectReason IS NOT NULL AND LEN(LTRIM(RTRIM(RejectReason))) > 0)),
    CONSTRAINT CK_Payments_OutgoingBankRef CHECK (Direction <> 'Out' OR Status <> 'Verified' OR Method <> 'BankTransfer' OR (BankRef IS NOT NULL AND LEN(LTRIM(RTRIM(BankRef))) > 0)),
    CONSTRAINT CK_Payments_ActorSeparation CHECK (NOT (Direction = 'In' AND Status = 'Verified' AND BankTransactionId IS NULL AND VerifiedByUserId = RecordedByUserId))
);
GO

CREATE UNIQUE INDEX UX_Payments_BankTransactionId
    ON dbo.Payments (BankTransactionId)
    WHERE BankTransactionId IS NOT NULL;
GO

CREATE UNIQUE INDEX UX_Payments_OneDeposit
    ON dbo.Payments (BookingId)
    WHERE Type = 'Deposit' AND Status <> 'Rejected';
GO

CREATE UNIQUE INDEX UX_Payments_OneRefund
    ON dbo.Payments (BookingId)
    WHERE Type = 'Refund' AND Status <> 'Rejected';
GO

CREATE UNIQUE INDEX UX_Payments_OneAdditionalDeposit
    ON dbo.Payments (ChangeRequestId)
    WHERE Type = 'AdditionalDeposit' AND Status <> 'Rejected';
GO

CREATE UNIQUE INDEX UX_Payments_OneCompensation
    ON dbo.Payments (BookingId)
    WHERE Type = 'Compensation' AND Status <> 'Rejected';
GO

CREATE TABLE dbo.BankTransactions
(
    Id INT IDENTITY(1,1) NOT NULL,
    Source VARCHAR(10) NOT NULL,
    SourceFileId UNIQUEIDENTIFIER NULL,
    AccountNumber VARCHAR(30) NOT NULL,
    BankReference VARCHAR(100) NOT NULL,
    Amount DECIMAL(18,0) NOT NULL,
    TransferContent NVARCHAR(500) NULL,
    SenderName NVARCHAR(100) NULL,
    SenderAccount VARCHAR(30) NULL,
    TransactionTime DATETIME2(3) NOT NULL,
    MatchStatus VARCHAR(20) NOT NULL,
    MatchedBookingId INT NULL,
    ProcessedByUserId INT NULL,
    ProcessedAt DATETIME2(3) NULL,
    RefundedAt DATETIME2(3) NULL,
    RefundBankRef VARCHAR(100) NULL,
    Note NVARCHAR(500) NULL,
    CreatedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_BankTransactions_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_BankTransactions PRIMARY KEY (Id),
    CONSTRAINT UQ_BankTransactions_AccountNumber_BankReference UNIQUE (AccountNumber, BankReference),
    CONSTRAINT CK_BankTransactions_Source CHECK (Source IN ('Import', 'Manual')),
    CONSTRAINT CK_BankTransactions_Amount CHECK (Amount > 0),
    CONSTRAINT CK_BankTransactions_MatchStatus CHECK (MatchStatus IN ('Unmatched', 'Matched', 'ManuallyMatched', 'Underpaid', 'LatePayment', 'Refunded', 'Ignored')),
    CONSTRAINT CK_BankTransactions_MatchedBooking CHECK (MatchStatus NOT IN ('Matched', 'ManuallyMatched', 'Underpaid', 'LatePayment') OR MatchedBookingId IS NOT NULL),
    CONSTRAINT CK_BankTransactions_ProcessedPair CHECK ((ProcessedByUserId IS NULL AND ProcessedAt IS NULL) OR (ProcessedByUserId IS NOT NULL AND ProcessedAt IS NOT NULL)),
    CONSTRAINT CK_BankTransactions_ManualProcessing CHECK (MatchStatus NOT IN ('ManuallyMatched', 'Refunded', 'Ignored') OR (ProcessedByUserId IS NOT NULL AND ProcessedAt IS NOT NULL)),
    CONSTRAINT CK_BankTransactions_Refund CHECK (MatchStatus <> 'Refunded' OR (RefundedAt IS NOT NULL AND RefundBankRef IS NOT NULL AND LEN(LTRIM(RTRIM(RefundBankRef))) > 0)),
    CONSTRAINT CK_BankTransactions_IgnoredNote CHECK (MatchStatus <> 'Ignored' OR (Note IS NOT NULL AND LEN(LTRIM(RTRIM(Note))) > 0))
);
GO

CREATE TABLE dbo.Settlements
(
    Id INT IDENTITY(1,1) NOT NULL,
    HomestayId INT NOT NULL,
    [Year] INT NOT NULL,
    [Month] INT NOT NULL,
    PeriodNo TINYINT NOT NULL,
    TotalRoomAmount DECIMAL(18,0) NOT NULL,
    TotalRetained DECIMAL(18,0) NOT NULL,
    TotalCommission DECIMAL(18,0) NOT NULL,
    TotalDiscount DECIMAL(18,0) NOT NULL,
    PayoutAmount DECIMAL(18,0) NOT NULL,
    Status VARCHAR(20) NOT NULL,
    SentAt DATETIME2(3) NULL,
    SentByUserId INT NULL,
    PaidAt DATETIME2(3) NULL,
    PaidByUserId INT NULL,
    PaidBankRef VARCHAR(100) NULL,
    OwnerFeedback NVARCHAR(500) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Settlements_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_Settlements PRIMARY KEY (Id),
    CONSTRAINT UQ_Settlements_HomestayId_Year_Month_PeriodNo UNIQUE (HomestayId, Year, Month, PeriodNo),
    CONSTRAINT CK_Settlements_Year CHECK ([Year] BETWEEN 2020 AND 2100),
    CONSTRAINT CK_Settlements_Month CHECK ([Month] BETWEEN 1 AND 12),
    CONSTRAINT CK_Settlements_PeriodNo CHECK (PeriodNo IN (1, 2)),
    CONSTRAINT CK_Settlements_Amounts CHECK (TotalRoomAmount >= 0 AND TotalRetained >= 0 AND TotalCommission >= 0 AND TotalDiscount >= 0 AND PayoutAmount >= 0),
    CONSTRAINT CK_Settlements_Commission CHECK (TotalCommission <= TotalRetained),
    CONSTRAINT CK_Settlements_Payout CHECK (PayoutAmount = TotalRetained - TotalCommission),
    CONSTRAINT CK_Settlements_Status CHECK (Status IN ('Draft', 'Sent', 'Paid')),
    CONSTRAINT CK_Settlements_Sending CHECK (Status = 'Draft' OR (SentAt IS NOT NULL AND SentByUserId IS NOT NULL)),
    CONSTRAINT CK_Settlements_Payment CHECK (Status <> 'Paid' OR (PaidAt IS NOT NULL AND PaidByUserId IS NOT NULL)),
    CONSTRAINT CK_Settlements_PaidBankRef CHECK (Status <> 'Paid' OR PayoutAmount = 0 OR (PaidBankRef IS NOT NULL AND LEN(LTRIM(RTRIM(PaidBankRef))) > 0))
);
GO

CREATE TABLE dbo.SettlementItems
(
    Id INT IDENTITY(1,1) NOT NULL,
    SettlementId INT NOT NULL,
    BookingId INT NOT NULL,
    ItemType VARCHAR(20) NOT NULL,
    RoomAmount DECIMAL(18,0) NOT NULL,
    DepositAmount DECIMAL(18,0) NOT NULL,
    RefundAmount DECIMAL(18,0) NOT NULL,
    RetainedAmount DECIMAL(18,0) NOT NULL,
    CommissionAmount DECIMAL(18,0) NOT NULL,
    DiscountAmount DECIMAL(18,0) NOT NULL,
    PayoutAmount DECIMAL(18,0) NOT NULL,
    CommissionRate DECIMAL(5,4) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_SettlementItems_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_SettlementItems PRIMARY KEY (Id),
    CONSTRAINT UQ_SettlementItems_BookingId UNIQUE (BookingId),
    CONSTRAINT CK_SettlementItems_ItemType CHECK (ItemType IN ('Completed', 'CancelledRetained', 'NoShow', 'FeeOnly')),
    CONSTRAINT CK_SettlementItems_Amounts CHECK (RoomAmount >= 0 AND DepositAmount >= 0 AND RefundAmount >= 0 AND RetainedAmount >= 0 AND CommissionAmount >= 0 AND DiscountAmount >= 0 AND PayoutAmount >= 0),
    CONSTRAINT CK_SettlementItems_Retained CHECK (RetainedAmount > 0 AND RetainedAmount <= DepositAmount),
    CONSTRAINT CK_SettlementItems_Commission CHECK (CommissionAmount <= RetainedAmount),
    CONSTRAINT CK_SettlementItems_Payout CHECK (PayoutAmount = RetainedAmount - CommissionAmount),
    CONSTRAINT CK_SettlementItems_CommissionRate CHECK (CommissionRate > 0 AND CommissionRate < 1)
);
GO

CREATE TABLE dbo.Notifications
(
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId INT NOT NULL,
    Message NVARCHAR(500) NOT NULL,
    RefType VARCHAR(30) NULL,
    RefId VARCHAR(30) NULL,
    IsRead BIT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    CONSTRAINT PK_Notifications PRIMARY KEY (Id)
);
GO

CREATE TABLE dbo.WorkTasks
(
    Id INT IDENTITY(1,1) NOT NULL,
    Title NVARCHAR(200) NOT NULL,
    Description NVARCHAR(1000) NULL,
    AssigneeEmployeeId INT NOT NULL,
    CreatedByUserId INT NOT NULL,
    BookingId INT NULL,
    HomestayId INT NULL,
    Priority TINYINT NOT NULL,
    DueAt DATETIME2(3) NULL,
    Status VARCHAR(20) NOT NULL,
    CompletedAt DATETIME2(3) NULL,
    ResultNote NVARCHAR(1000) NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_WorkTasks_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2(3) NULL,
    RowVer ROWVERSION NOT NULL,
    CONSTRAINT PK_WorkTasks PRIMARY KEY (Id),
    CONSTRAINT CK_WorkTasks_Priority CHECK (Priority IN (1, 2, 3)),
    CONSTRAINT CK_WorkTasks_Status CHECK (Status IN ('Open', 'Done', 'Cancelled')),
    CONSTRAINT CK_WorkTasks_Completion CHECK (Status <> 'Done' OR CompletedAt IS NOT NULL)
);
GO

CREATE TABLE dbo.AuditLogs
(
    Id BIGINT IDENTITY(1,1) NOT NULL,
    UserId INT NULL,
    ActionCode VARCHAR(50) NOT NULL,
    TableName VARCHAR(50) NOT NULL,
    RecordId VARCHAR(50) NOT NULL,
    OldValues NVARCHAR(MAX) NULL,
    NewValues NVARCHAR(MAX) NULL,
    Note NVARCHAR(500) NULL,
    MachineName NVARCHAR(100) NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_AuditLogs PRIMARY KEY (Id)
);
GO

CREATE TABLE dbo.StoredFiles
(
    Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_StoredFiles_Id DEFAULT NEWSEQUENTIALID(),
    Category VARCHAR(20) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    ContentType VARCHAR(100) NOT NULL,
    SizeBytes INT NOT NULL,
    Sha256 BINARY(32) NOT NULL,
    Content VARBINARY(MAX) NOT NULL,
    UploadedByUserId INT NOT NULL,
    CreatedAt DATETIME2(3) NOT NULL CONSTRAINT DF_StoredFiles_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_StoredFiles PRIMARY KEY (Id),
    CONSTRAINT CK_StoredFiles_Category CHECK (Category IN ('HomestayImage', 'Evidence', 'Document', 'Statement')),
    CONSTRAINT CK_StoredFiles_SizeBytes CHECK (SizeBytes > 0 AND SizeBytes <= 5242880),
    CONSTRAINT CK_StoredFiles_ContentLength CHECK (DATALENGTH(Content) = SizeBytes)
);
GO

-- Thêm FK sau khi đủ bảng để xử lý các quan hệ vòng.

ALTER TABLE dbo.Employees WITH CHECK
    ADD CONSTRAINT FK_Employees_UserId FOREIGN KEY (UserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.EmployeeAreas WITH CHECK
    ADD CONSTRAINT FK_EmployeeAreas_EmployeeId FOREIGN KEY (EmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.EmployeeAreas WITH CHECK
    ADD CONSTRAINT FK_EmployeeAreas_AreaId FOREIGN KEY (AreaId)
    REFERENCES dbo.Areas (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.EmployeeAreas WITH CHECK
    ADD CONSTRAINT FK_EmployeeAreas_AssignedByEmployeeId FOREIGN KEY (AssignedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Owners WITH CHECK
    ADD CONSTRAINT FK_Owners_ApprovedByEmployeeId FOREIGN KEY (ApprovedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Owners WITH CHECK
    ADD CONSTRAINT FK_Owners_CreatedByEmployeeId FOREIGN KEY (CreatedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.UserSessions WITH CHECK
    ADD CONSTRAINT FK_UserSessions_UserId FOREIGN KEY (UserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.SystemSettings WITH CHECK
    ADD CONSTRAINT FK_SystemSettings_UpdatedByUserId FOREIGN KEY (UpdatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Homestays WITH CHECK
    ADD CONSTRAINT FK_Homestays_OwnerId FOREIGN KEY (OwnerId)
    REFERENCES dbo.Owners (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Homestays WITH CHECK
    ADD CONSTRAINT FK_Homestays_AreaId FOREIGN KEY (AreaId)
    REFERENCES dbo.Areas (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Homestays WITH CHECK
    ADD CONSTRAINT FK_Homestays_ApprovedByEmployeeId FOREIGN KEY (ApprovedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Homestays WITH CHECK
    ADD CONSTRAINT FK_Homestays_CreatedByUserId FOREIGN KEY (CreatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.HomestayAmenities WITH CHECK
    ADD CONSTRAINT FK_HomestayAmenities_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.HomestayAmenities WITH CHECK
    ADD CONSTRAINT FK_HomestayAmenities_AmenityId FOREIGN KEY (AmenityId)
    REFERENCES dbo.Amenities (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.HomestayImages WITH CHECK
    ADD CONSTRAINT FK_HomestayImages_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.HomestayImages WITH CHECK
    ADD CONSTRAINT FK_HomestayImages_RoomTypeId FOREIGN KEY (RoomTypeId)
    REFERENCES dbo.RoomTypes (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.HomestayImages WITH CHECK
    ADD CONSTRAINT FK_HomestayImages_FileId FOREIGN KEY (FileId)
    REFERENCES dbo.StoredFiles (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.VerificationDocuments WITH CHECK
    ADD CONSTRAINT FK_VerificationDocuments_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.VerificationDocuments WITH CHECK
    ADD CONSTRAINT FK_VerificationDocuments_FileId FOREIGN KEY (FileId)
    REFERENCES dbo.StoredFiles (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.VerificationDocuments WITH CHECK
    ADD CONSTRAINT FK_VerificationDocuments_UploadedByUserId FOREIGN KEY (UploadedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Contracts WITH CHECK
    ADD CONSTRAINT FK_Contracts_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Contracts WITH CHECK
    ADD CONSTRAINT FK_Contracts_SignedByEmployeeId FOREIGN KEY (SignedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.CancellationPolicies WITH CHECK
    ADD CONSTRAINT FK_CancellationPolicies_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.CancellationRules WITH CHECK
    ADD CONSTRAINT FK_CancellationRules_PolicyId FOREIGN KEY (PolicyId)
    REFERENCES dbo.CancellationPolicies (Id) ON DELETE CASCADE;
GO

ALTER TABLE dbo.RoomTypes WITH CHECK
    ADD CONSTRAINT FK_RoomTypes_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomTypes WITH CHECK
    ADD CONSTRAINT FK_RoomTypes_CategoryId FOREIGN KEY (CategoryId)
    REFERENCES dbo.RoomCategories (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Rooms WITH CHECK
    ADD CONSTRAINT FK_Rooms_RoomTypeId FOREIGN KEY (RoomTypeId)
    REFERENCES dbo.RoomTypes (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomTypePrices WITH CHECK
    ADD CONSTRAINT FK_RoomTypePrices_RoomTypeId FOREIGN KEY (RoomTypeId)
    REFERENCES dbo.RoomTypes (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomTypePrices WITH CHECK
    ADD CONSTRAINT FK_RoomTypePrices_CreatedByUserId FOREIGN KEY (CreatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomBlocks WITH CHECK
    ADD CONSTRAINT FK_RoomBlocks_RoomId FOREIGN KEY (RoomId)
    REFERENCES dbo.Rooms (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomBlocks WITH CHECK
    ADD CONSTRAINT FK_RoomBlocks_CreatedByUserId FOREIGN KEY (CreatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomNights WITH CHECK
    ADD CONSTRAINT FK_RoomNights_RoomId FOREIGN KEY (RoomId)
    REFERENCES dbo.Rooms (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomNights WITH CHECK
    ADD CONSTRAINT FK_RoomNights_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.RoomNights WITH CHECK
    ADD CONSTRAINT FK_RoomNights_RoomBlockId FOREIGN KEY (RoomBlockId)
    REFERENCES dbo.RoomBlocks (Id) ON DELETE CASCADE;
GO

ALTER TABLE dbo.RoomNights WITH CHECK
    ADD CONSTRAINT FK_RoomNights_ChangeRequestId FOREIGN KEY (ChangeRequestId)
    REFERENCES dbo.BookingChangeRequests (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Customers WITH CHECK
    ADD CONSTRAINT FK_Customers_CreatedByEmployeeId FOREIGN KEY (CreatedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_CustomerId FOREIGN KEY (CustomerId)
    REFERENCES dbo.Customers (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_CreatedByEmployeeId FOREIGN KEY (CreatedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_AssignedEmployeeId FOREIGN KEY (AssignedEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_RoomTypeId FOREIGN KEY (RoomTypeId)
    REFERENCES dbo.RoomTypes (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_RoomId FOREIGN KEY (RoomId)
    REFERENCES dbo.Rooms (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_VoucherId FOREIGN KEY (VoucherId)
    REFERENCES dbo.Vouchers (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_CancellationPolicyId FOREIGN KEY (CancellationPolicyId)
    REFERENCES dbo.CancellationPolicies (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Bookings WITH CHECK
    ADD CONSTRAINT FK_Bookings_ReplacedByBookingId FOREIGN KEY (ReplacedByBookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingNights WITH CHECK
    ADD CONSTRAINT FK_BookingNights_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingStatusHistories WITH CHECK
    ADD CONSTRAINT FK_BookingStatusHistories_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingStatusHistories WITH CHECK
    ADD CONSTRAINT FK_BookingStatusHistories_ActorUserId FOREIGN KEY (ActorUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_NewRoomTypeId FOREIGN KEY (NewRoomTypeId)
    REFERENCES dbo.RoomTypes (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_NewRoomId FOREIGN KEY (NewRoomId)
    REFERENCES dbo.Rooms (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_RequestedByUserId FOREIGN KEY (RequestedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_DecidedByUserId FOREIGN KEY (DecidedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BookingChangeRequests WITH CHECK
    ADD CONSTRAINT FK_BookingChangeRequests_FinanceConfirmedByUserId FOREIGN KEY (FinanceConfirmedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Vouchers WITH CHECK
    ADD CONSTRAINT FK_Vouchers_CustomerId FOREIGN KEY (CustomerId)
    REFERENCES dbo.Customers (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Vouchers WITH CHECK
    ADD CONSTRAINT FK_Vouchers_SourceBookingId FOREIGN KEY (SourceBookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Vouchers WITH CHECK
    ADD CONSTRAINT FK_Vouchers_IssuedByEmployeeId FOREIGN KEY (IssuedByEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_ChangeRequestId FOREIGN KEY (ChangeRequestId)
    REFERENCES dbo.BookingChangeRequests (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_BankTransactionId FOREIGN KEY (BankTransactionId)
    REFERENCES dbo.BankTransactions (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_EvidenceFileId FOREIGN KEY (EvidenceFileId)
    REFERENCES dbo.StoredFiles (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_RecordedByUserId FOREIGN KEY (RecordedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Payments WITH CHECK
    ADD CONSTRAINT FK_Payments_VerifiedByUserId FOREIGN KEY (VerifiedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BankTransactions WITH CHECK
    ADD CONSTRAINT FK_BankTransactions_SourceFileId FOREIGN KEY (SourceFileId)
    REFERENCES dbo.StoredFiles (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BankTransactions WITH CHECK
    ADD CONSTRAINT FK_BankTransactions_MatchedBookingId FOREIGN KEY (MatchedBookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BankTransactions WITH CHECK
    ADD CONSTRAINT FK_BankTransactions_ProcessedByUserId FOREIGN KEY (ProcessedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.BankTransactions WITH CHECK
    ADD CONSTRAINT FK_BankTransactions_CreatedByUserId FOREIGN KEY (CreatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Settlements WITH CHECK
    ADD CONSTRAINT FK_Settlements_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Settlements WITH CHECK
    ADD CONSTRAINT FK_Settlements_SentByUserId FOREIGN KEY (SentByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Settlements WITH CHECK
    ADD CONSTRAINT FK_Settlements_PaidByUserId FOREIGN KEY (PaidByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.SettlementItems WITH CHECK
    ADD CONSTRAINT FK_SettlementItems_SettlementId FOREIGN KEY (SettlementId)
    REFERENCES dbo.Settlements (Id) ON DELETE CASCADE;
GO

ALTER TABLE dbo.SettlementItems WITH CHECK
    ADD CONSTRAINT FK_SettlementItems_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.Notifications WITH CHECK
    ADD CONSTRAINT FK_Notifications_UserId FOREIGN KEY (UserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.WorkTasks WITH CHECK
    ADD CONSTRAINT FK_WorkTasks_AssigneeEmployeeId FOREIGN KEY (AssigneeEmployeeId)
    REFERENCES dbo.Employees (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.WorkTasks WITH CHECK
    ADD CONSTRAINT FK_WorkTasks_CreatedByUserId FOREIGN KEY (CreatedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.WorkTasks WITH CHECK
    ADD CONSTRAINT FK_WorkTasks_BookingId FOREIGN KEY (BookingId)
    REFERENCES dbo.Bookings (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.WorkTasks WITH CHECK
    ADD CONSTRAINT FK_WorkTasks_HomestayId FOREIGN KEY (HomestayId)
    REFERENCES dbo.Homestays (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.AuditLogs WITH CHECK
    ADD CONSTRAINT FK_AuditLogs_UserId FOREIGN KEY (UserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

ALTER TABLE dbo.StoredFiles WITH CHECK
    ADD CONSTRAINT FK_StoredFiles_UploadedByUserId FOREIGN KEY (UploadedByUserId)
    REFERENCES dbo.Users (Id) ON DELETE NO ACTION;
GO

INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (1, N'001_schema.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO


-- 002_indexes.sql
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
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 2)
    SET NOEXEC ON;
GO
BEGIN TRANSACTION;
GO

CREATE INDEX IX_RoomNights_BookingId
    ON dbo.RoomNights (BookingId);
GO

CREATE INDEX IX_RoomNights_RoomBlockId
    ON dbo.RoomNights (RoomBlockId);
GO

CREATE INDEX IX_RoomNights_ChangeRequestId
    ON dbo.RoomNights (ChangeRequestId);
GO

CREATE INDEX IX_Bookings_CustomerId
    ON dbo.Bookings (CustomerId);
GO

CREATE INDEX IX_Bookings_HomestayId_CheckIn
    ON dbo.Bookings (HomestayId, CheckIn);
GO

CREATE INDEX IX_Bookings_Status_HoldExpiresAt
    ON dbo.Bookings (Status, HoldExpiresAt);
GO

CREATE INDEX IX_Bookings_Status_ConfirmDeadlineAt
    ON dbo.Bookings (Status, ConfirmDeadlineAt);
GO

CREATE INDEX IX_Bookings_AssignedEmployeeId_Status
    ON dbo.Bookings (AssignedEmployeeId, Status);
GO

CREATE INDEX IX_Bookings_CreatedByEmployeeId_CreatedAt
    ON dbo.Bookings (CreatedByEmployeeId, CreatedAt);
GO

CREATE INDEX IX_Bookings_CheckOut_Status
    ON dbo.Bookings (CheckOut, Status);
GO

CREATE INDEX IX_Customers_FullName
    ON dbo.Customers (FullName);
GO

CREATE INDEX IX_Homestays_AreaId_Status
    ON dbo.Homestays (AreaId, Status);
GO

CREATE INDEX IX_Homestays_OwnerId
    ON dbo.Homestays (OwnerId);
GO

CREATE INDEX IX_Payments_BookingId
    ON dbo.Payments (BookingId);
GO

CREATE INDEX IX_Payments_Status_Type_RecordedAt
    ON dbo.Payments (Status, Type, RecordedAt);
GO

CREATE INDEX IX_BankTransactions_MatchStatus_TransactionTime
    ON dbo.BankTransactions (MatchStatus, TransactionTime);
GO

CREATE INDEX IX_BankTransactions_MatchedBookingId
    ON dbo.BankTransactions (MatchedBookingId);
GO

CREATE INDEX IX_Settlements_Status
    ON dbo.Settlements (Status);
GO

CREATE INDEX IX_Notifications_UserId_IsRead_CreatedAt
    ON dbo.Notifications (UserId, IsRead, CreatedAt);
GO

CREATE INDEX IX_AuditLogs_TableName_RecordId
    ON dbo.AuditLogs (TableName, RecordId);
GO

CREATE INDEX IX_AuditLogs_UserId_CreatedAt
    ON dbo.AuditLogs (UserId, CreatedAt);
GO

CREATE INDEX IX_WorkTasks_AssigneeEmployeeId_Status_DueAt
    ON dbo.WorkTasks (AssigneeEmployeeId, Status, DueAt);
GO

INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (2, N'002_indexes.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO


-- 003_views.sql
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
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 3)
    SET NOEXEC ON;
GO
BEGIN TRANSACTION;
GO
-- Hoàn tiền ưu tiên hơn thu cọc; gom từng nguồn trước để mỗi đơn chỉ có một dòng.
CREATE VIEW dbo.vw_BookingPaymentState
AS
WITH PaymentTotals AS
(
    SELECT BookingId,
        MAX(CASE WHEN Type = 'Deposit' AND Status = 'Pending' THEN 1 ELSE 0 END) AS HasPendingDeposit,
        MAX(CASE WHEN Type = 'Deposit' AND Status = 'Verified' THEN 1 ELSE 0 END) AS HasVerifiedDeposit,
        MAX(CASE WHEN Type = 'Refund' AND Status = 'Pending' THEN 1 ELSE 0 END) AS HasPendingRefund,
        MAX(CASE WHEN Type = 'Refund' AND Status = 'Verified' THEN 1 ELSE 0 END) AS HasVerifiedRefund,
        SUM(CASE WHEN Type = 'Deposit' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedDepositAmount,
        SUM(CASE WHEN Type = 'AdditionalDeposit' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedAdditionalDepositAmount,
        SUM(CASE WHEN Type = 'Refund' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedRefundAmount
    FROM dbo.Payments
    GROUP BY BookingId
), OpenChanges AS
(
    SELECT BookingId, MAX(CASE WHEN Status = 'AwaitingPayment' THEN 1 ELSE 0 END) AS HasAwaitingChange
    FROM dbo.BookingChangeRequests
    GROUP BY BookingId
), States AS
(
    SELECT b.Id AS BookingId, b.Code AS BookingCode, b.Status AS BookingStatus,
        b.DepositAmount - b.DiscountAmount AS AmountDue,
        ISNULL(p.VerifiedDepositAmount, 0) AS VerifiedDepositAmount,
        ISNULL(p.VerifiedAdditionalDepositAmount, 0) AS VerifiedAdditionalDepositAmount,
        ISNULL(p.VerifiedRefundAmount, 0) AS VerifiedRefundAmount,
        CASE
            WHEN p.HasPendingRefund = 1 THEN 'RefundPending'
            WHEN p.HasVerifiedRefund = 1 THEN 'Refunded'
            WHEN c.HasAwaitingChange = 1 THEN 'AdditionalDepositPending'
            WHEN b.Status IN ('Cancelled', 'NoShow') AND p.HasVerifiedDeposit = 1 AND b.RefundAmount = 0 THEN 'DepositRetained'
            WHEN p.HasPendingDeposit = 1 THEN 'DepositPending'
            WHEN p.HasVerifiedDeposit = 1 THEN 'DepositVerified'
            WHEN b.Status IN ('Expired', 'Cancelled') THEN 'NoFinancialActivity'
            ELSE 'DepositNotCollected'
        END AS PaymentState
    FROM dbo.Bookings AS b
    LEFT JOIN PaymentTotals AS p ON p.BookingId = b.Id
    LEFT JOIN OpenChanges AS c ON c.BookingId = b.Id
)
SELECT BookingId, BookingCode, BookingStatus, AmountDue,
    VerifiedDepositAmount, VerifiedAdditionalDepositAmount, VerifiedRefundAmount, PaymentState,
    CASE PaymentState
        WHEN 'RefundPending' THEN N'Chờ hoàn tiền'
        WHEN 'Refunded' THEN
            CASE WHEN VerifiedRefundAmount >= AmountDue THEN N'Đã hoàn tiền (đủ)' ELSE N'Đã hoàn tiền (một phần)' END
        WHEN 'AdditionalDepositPending' THEN N'Chờ thu cọc bổ sung'
        WHEN 'DepositRetained' THEN N'Giữ cọc (không hoàn)'
        WHEN 'DepositPending' THEN N'Chờ xác minh cọc'
        WHEN 'DepositVerified' THEN N'Đã thu cọc'
        WHEN 'NoFinancialActivity' THEN N'Không phát sinh'
        ELSE N'Chưa thu cọc'
    END AS PaymentStateName,
    CASE WHEN PaymentState = 'Refunded'
        THEN CASE WHEN VerifiedRefundAmount >= AmountDue THEN 'Full' ELSE 'Partial' END
        ELSE NULL
    END AS RefundExtent
FROM States;
GO
CREATE VIEW dbo.vw_CashBook
AS
SELECT CAST('Payment' AS VARCHAR(20)) AS SourceType, p.Id AS SourceId,
    p.VerifiedAt AS EntryAt, CAST(p.VoucherNo AS VARCHAR(100)) AS ReferenceNo,
    CAST(p.Type AS VARCHAR(30)) AS EntryType,
    CAST(CASE WHEN p.Direction = 'In' THEN p.Amount ELSE 0 END AS DECIMAL(18,0)) AS IncomeAmount,
    CAST(CASE WHEN p.Direction = 'Out' THEN p.Amount ELSE 0 END AS DECIMAL(18,0)) AS ExpenseAmount,
    CAST(COALESCE(p.PayerOrPayeeName, c.FullName) AS NVARCHAR(200)) AS PartyName,
    p.RecordedByUserId AS RecordedByUserId, u.Username AS RecordedByUsername,
    p.BookingId, b.HomestayId, p.Note
FROM dbo.Payments AS p
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
INNER JOIN dbo.Customers AS c ON c.Id = b.CustomerId
INNER JOIN dbo.Users AS u ON u.Id = p.RecordedByUserId
WHERE p.Status = 'Verified'
UNION ALL
SELECT 'Settlement', s.Id, s.PaidAt,
    COALESCE(s.PaidBankRef, CONCAT('SET-', s.Id)), 'OwnerPayout',
    CAST(0 AS DECIMAL(18,0)), s.PayoutAmount,
    o.FullName, s.PaidByUserId, u.Username,
    CAST(NULL AS INT), s.HomestayId, s.OwnerFeedback
FROM dbo.Settlements AS s
INNER JOIN dbo.Homestays AS h ON h.Id = s.HomestayId
INNER JOIN dbo.Owners AS o ON o.Id = h.OwnerId
INNER JOIN dbo.Users AS u ON u.Id = s.PaidByUserId
WHERE s.Status = 'Paid'
UNION ALL
SELECT 'BankTransaction', bt.Id, bt.RefundedAt, bt.RefundBankRef, 'UnmatchedRefund',
    CAST(0 AS DECIMAL(18,0)), bt.Amount,
    bt.SenderName, bt.ProcessedByUserId, u.Username,
    bt.MatchedBookingId, b.HomestayId, bt.Note
FROM dbo.BankTransactions AS bt
LEFT JOIN dbo.Bookings AS b ON b.Id = bt.MatchedBookingId
LEFT JOIN dbo.Users AS u ON u.Id = bt.ProcessedByUserId
WHERE bt.MatchStatus = 'Refunded';
GO
CREATE VIEW dbo.vw_Payables
AS
SELECT CAST('Settlement' AS VARCHAR(20)) AS SourceType, s.Id AS SourceId,
    s.SentAt AS PayableAt,
    CAST(CONCAT('SET-', s.Id) AS VARCHAR(100)) AS ReferenceNo,
    CAST('OwnerPayout' AS VARCHAR(30)) AS PayableType,
    s.PayoutAmount AS Amount,
    CAST(o.FullName AS NVARCHAR(200)) AS PartyName,
    CAST(NULL AS INT) AS BookingId, s.HomestayId,
    s.SentByUserId AS RecordedByUserId, s.OwnerFeedback AS Note
FROM dbo.Settlements AS s
INNER JOIN dbo.Homestays AS h ON h.Id = s.HomestayId
INNER JOIN dbo.Owners AS o ON o.Id = h.OwnerId
WHERE s.Status = 'Sent'
UNION ALL
SELECT 'Payment', p.Id, p.RecordedAt, p.VoucherNo, p.Type,
    p.Amount, COALESCE(p.PayerOrPayeeName, c.FullName),
    p.BookingId, b.HomestayId, p.RecordedByUserId, p.Note
FROM dbo.Payments AS p
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
INNER JOIN dbo.Customers AS c ON c.Id = b.CustomerId
WHERE p.Direction = 'Out' AND p.Type IN ('Refund', 'Compensation') AND p.Status = 'Pending'
UNION ALL
-- Note chưa có cấu trúc: chỉ nhận diện chuyển dư khi số tiền vượt nghĩa vụ của phiếu đã khớp.
SELECT 'BankTransaction', bt.Id, bt.TransactionTime, bt.BankReference, 'ExcessTransfer',
    CAST(bt.Amount - due.ExpectedAmount AS DECIMAL(18,0)),
    bt.SenderName, p.BookingId, b.HomestayId, bt.CreatedByUserId, bt.Note
FROM dbo.BankTransactions AS bt
INNER JOIN dbo.Payments AS p ON p.BankTransactionId = bt.Id
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
LEFT JOIN dbo.BookingChangeRequests AS cr ON cr.Id = p.ChangeRequestId
CROSS APPLY
(
    SELECT CASE WHEN p.Type = 'AdditionalDeposit' THEN cr.AdditionalDeposit
        ELSE b.DepositAmount - b.DiscountAmount END AS ExpectedAmount
) AS due
WHERE bt.MatchStatus IN ('Matched', 'ManuallyMatched')
    AND p.Direction = 'In' AND p.Status = 'Verified'
    AND bt.Note IS NOT NULL AND LEN(LTRIM(RTRIM(bt.Note))) > 0
    AND due.ExpectedAmount IS NOT NULL AND bt.Amount > due.ExpectedAmount;
GO
INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (3, N'003_views.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO


-- 004_security.sql
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
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 4)
    SET NOEXEC ON;
GO
BEGIN TRANSACTION;
GO
IF DATABASE_PRINCIPAL_ID(N'app_homestay') IS NULL
    CREATE ROLE app_homestay AUTHORIZATION dbo;
GO
IF SUSER_ID(N'homestay_app') IS NULL
BEGIN
    CREATE LOGIN homestay_app WITH PASSWORD = '$(AppSqlPassword)',
        CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
END;
GO
IF DATABASE_PRINCIPAL_ID(N'homestay_app') IS NULL
    CREATE USER homestay_app FOR LOGIN homestay_app WITH DEFAULT_SCHEMA = dbo;
GO
IF NOT EXISTS
(
    SELECT 1
    FROM sys.database_role_members AS rm
    WHERE rm.role_principal_id = DATABASE_PRINCIPAL_ID(N'app_homestay')
        AND rm.member_principal_id = DATABASE_PRINCIPAL_ID(N'homestay_app')
)
    ALTER ROLE app_homestay ADD MEMBER homestay_app;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Users TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Users TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Employees TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Employees TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.EmployeeAreas TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.EmployeeAreas TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Owners TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Owners TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Areas TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Areas TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Amenities TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Amenities TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.RoomCategories TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.RoomCategories TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Holidays TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Holidays TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SystemSettings TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.SystemSettings TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Counters TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Counters TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Homestays TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Homestays TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.HomestayAmenities TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.HomestayAmenities TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.HomestayImages TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.HomestayImages TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.VerificationDocuments TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.VerificationDocuments TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Contracts TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Contracts TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.CancellationPolicies TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.CancellationPolicies TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.CancellationRules TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.CancellationRules TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.RoomTypes TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.RoomTypes TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Rooms TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Rooms TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.RoomTypePrices TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.RoomTypePrices TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.RoomBlocks TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.RoomBlocks TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.RoomNights TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.RoomNights TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Customers TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Customers TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Bookings TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Bookings TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.BookingNights TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.BookingNights TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.BookingChangeRequests TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.BookingChangeRequests TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Vouchers TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Vouchers TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Payments TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Payments TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.BankTransactions TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.BankTransactions TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Settlements TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.Settlements TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SettlementItems TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.SettlementItems TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.Notifications TO app_homestay;
GO

GRANT DELETE ON OBJECT::dbo.Notifications TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.WorkTasks TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.WorkTasks TO app_homestay;
GO

GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.StoredFiles TO app_homestay;
GO

DENY DELETE ON OBJECT::dbo.StoredFiles TO app_homestay;
GO

GRANT SELECT, INSERT ON OBJECT::dbo.AuditLogs TO app_homestay;
GO
DENY UPDATE, DELETE ON OBJECT::dbo.AuditLogs TO app_homestay;
GO

GRANT SELECT, INSERT ON OBJECT::dbo.BookingStatusHistories TO app_homestay;
GO
DENY UPDATE, DELETE ON OBJECT::dbo.BookingStatusHistories TO app_homestay;
GO

GRANT SELECT, INSERT ON OBJECT::dbo.UserSessions TO app_homestay;
GO
DENY UPDATE, DELETE ON OBJECT::dbo.UserSessions TO app_homestay;
GO
-- SQL Server cho phép GRANT theo cột ghi đè DENY UPDATE ở mức bảng.
GRANT UPDATE (LastSeenAt, EndedAt, EndReason) ON OBJECT::dbo.UserSessions TO app_homestay;
GO
GRANT SELECT ON OBJECT::dbo.SchemaVersions TO app_homestay;
GO
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SchemaVersions TO app_homestay;
GO

GRANT SELECT ON OBJECT::dbo.vw_BookingPaymentState TO app_homestay;
GO
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.vw_BookingPaymentState TO app_homestay;
GO

GRANT SELECT ON OBJECT::dbo.vw_CashBook TO app_homestay;
GO
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.vw_CashBook TO app_homestay;
GO

GRANT SELECT ON OBJECT::dbo.vw_Payables TO app_homestay;
GO
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.vw_Payables TO app_homestay;
GO

INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (4, N'004_security.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO

