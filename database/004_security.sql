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
