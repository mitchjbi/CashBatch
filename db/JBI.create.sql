-- Create JBI database and required tables for the CashBatch write store
-- Adjust server/paths as needed for your environment

IF DB_ID(N'JBI') IS NULL
BEGIN
    PRINT 'Creating database JBI...';
    CREATE DATABASE JBI;
END
GO

USE JBI;
GO

-- Batches table
IF OBJECT_ID(N'dbo.Batches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Batches
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ImportedAt DATETIME2(0) NOT NULL,
        ImportedBy NVARCHAR(100) NOT NULL,
        SourceFilename NVARCHAR(260) NOT NULL,
        Status INT NOT NULL
    );
END
GO

-- Payments table
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Payments
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        BatchId UNIQUEIDENTIFIER NOT NULL,
        CustomerId NVARCHAR(50) NULL,
        Amount DECIMAL(18,2) NOT NULL,
        CheckNumber NVARCHAR(50) NOT NULL,
        BankAccount NVARCHAR(100) NULL,
        RemitAddressHash NVARCHAR(64) NULL,
        Status INT NOT NULL,
        CONSTRAINT FK_Payments_Batches FOREIGN KEY (BatchId)
            REFERENCES dbo.Batches(Id)
            ON DELETE CASCADE
    );
    CREATE INDEX IX_Payments_BatchId ON dbo.Payments(BatchId);
END
GO

-- PaymentLines table
IF OBJECT_ID(N'dbo.PaymentLines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PaymentLines
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        PaymentId UNIQUEIDENTIFIER NOT NULL,
        InvoiceNo NVARCHAR(50) NULL,
        AppliedAmount DECIMAL(18,2) NOT NULL,
        WasAutoMatched BIT NOT NULL,
        CONSTRAINT FK_PaymentLines_Payments FOREIGN KEY (PaymentId)
            REFERENCES dbo.Payments(Id)
            ON DELETE CASCADE
    );
    CREATE INDEX IX_PaymentLines_PaymentId ON dbo.PaymentLines(PaymentId);
END
GO

-- CustomerLookups table
IF OBJECT_ID(N'dbo.CustomerLookups', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerLookups
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        KeyType NVARCHAR(50) NOT NULL,
        KeyValue NVARCHAR(200) NOT NULL,
        CustomerId NVARCHAR(50) NOT NULL,
        Confidence FLOAT NOT NULL,
        CreatedAt DATETIME2(0) NOT NULL,
        CONSTRAINT UX_CustomerLookups_Key UNIQUE (KeyType, KeyValue)
    );
END
GO

-- MatchLogs table
IF OBJECT_ID(N'dbo.MatchLogs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MatchLogs
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        PaymentId UNIQUEIDENTIFIER NOT NULL,
        Level NVARCHAR(20) NOT NULL,
        Message NVARCHAR(MAX) NOT NULL,
        CreatedAt DATETIME2(0) NOT NULL,
        CONSTRAINT FK_MatchLogs_Payments FOREIGN KEY (PaymentId)
            REFERENCES dbo.Payments(Id)
            ON DELETE CASCADE
    );
    CREATE INDEX IX_MatchLogs_PaymentId ON dbo.MatchLogs(PaymentId);
END
GO
