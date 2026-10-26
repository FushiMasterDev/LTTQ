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
