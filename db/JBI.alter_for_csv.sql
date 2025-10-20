-- Bring existing JBI tables in line with the CSV columns
-- Safe-guarded with existence checks. Run against JBI database.

USE [JBI];
GO

-- 1) cash_batches: add DepositDate and CustomerBatchNumber
IF COL_LENGTH('dbo.cash_batches', 'DepositDate') IS NULL
BEGIN
    ALTER TABLE dbo.cash_batches
        ADD [DepositDate] DATETIME2(0) NULL;
END
GO

IF COL_LENGTH('dbo.cash_batches', 'CustomerBatchNumber') IS NULL
BEGIN
    ALTER TABLE dbo.cash_batches
        ADD [CustomerBatchNumber] NVARCHAR(50) NULL;
END
GO

-- 2) cash_payments: add CSV-derived columns
IF COL_LENGTH('dbo.cash_payments', 'SequenceNumber') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [SequenceNumber] INT NOT NULL CONSTRAINT DF_cash_payments_SequenceNumber DEFAULT(0);
END
GO

IF COL_LENGTH('dbo.cash_payments', 'BankNumber') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [BankNumber] NVARCHAR(20) NULL;
END
GO

IF COL_LENGTH('dbo.cash_payments', 'AccountNumber') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [AccountNumber] NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.cash_payments', 'RemitterName') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [RemitterName] NVARCHAR(200) NULL;
END
GO

IF COL_LENGTH('dbo.cash_payments', 'TransactionType') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [TransactionType] NVARCHAR(50) NULL;
END
GO

IF COL_LENGTH('dbo.cash_payments', 'Category') IS NULL
BEGIN
    ALTER TABLE dbo.cash_payments
        ADD [Category] NVARCHAR(50) NULL;
END
GO

-- Optional: index to speed up ordering/queries by batch & sequence
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes WHERE name = 'IX_cash_payments_BatchId_SequenceNumber' AND object_id = OBJECT_ID('dbo.cash_payments')
)
BEGIN
    CREATE INDEX IX_cash_payments_BatchId_SequenceNumber ON dbo.cash_payments(BatchId, SequenceNumber);
END
GO
