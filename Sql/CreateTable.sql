-- CJM Apply Assistant — single-table schema for Azure SQL
IF OBJECT_ID(N'dbo.Jobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Jobs
    (
        JobKey            NVARCHAR(450)  NOT NULL CONSTRAINT PK_Jobs PRIMARY KEY,
        JobTitle          NVARCHAR(500)  NOT NULL,
        Company           NVARCHAR(300)  NOT NULL,
        Location          NVARCHAR(300)  NULL,
        Viewed            BIT            NOT NULL CONSTRAINT DF_Jobs_Viewed DEFAULT (0),
        PostedDateText    NVARCHAR(100)  NULL,
        PostedDate        DATE           NULL,
        IsTopApplicant    BIT            NOT NULL CONSTRAINT DF_Jobs_TopApplicant DEFAULT (0),
        ApplyUrl          NVARCHAR(1000) NULL,
        CompensationText  NVARCHAR(200)  NULL,
        CompSpecified     NVARCHAR(200)  NULL,
        ResumeUsed        NVARCHAR(300)  NULL,
        AppliedAt         DATETIME2      NULL,
        RawPaste          NVARCHAR(MAX)  NULL,
        CreatedAt         DATETIME2      NOT NULL CONSTRAINT DF_Jobs_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt         DATETIME2      NOT NULL CONSTRAINT DF_Jobs_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );
END
GO
