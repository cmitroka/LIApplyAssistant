using CjmApply.Core.Models;
using Microsoft.Data.SqlClient;

namespace CjmApplyAssistant.Data;

public sealed class JobRepository(string connectionString)
{
    private readonly string _connectionString = connectionString
        ?? throw new ArgumentNullException(nameof(connectionString));

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
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
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection);
        _ = await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<JobRecord?> GetByKeyAsync(string jobKey, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT JobKey, JobTitle, Company, Location, Viewed, PostedDateText, PostedDate,
                   IsTopApplicant, ApplyUrl, CompensationText, CompSpecified, ResumeUsed,
                   AppliedAt, RawPaste, CreatedAt, UpdatedAt
            FROM dbo.Jobs
            WHERE JobKey = @JobKey
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@JobKey", jobKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return Map(reader);
    }

    public async Task<IReadOnlyList<JobRecord>> ListRecentAsync(int take = 100, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                   JobKey, JobTitle, Company, Location, Viewed, PostedDateText, PostedDate,
                   IsTopApplicant, ApplyUrl, CompensationText, CompSpecified, ResumeUsed,
                   AppliedAt, RawPaste, CreatedAt, UpdatedAt
            FROM dbo.Jobs
            ORDER BY UpdatedAt DESC
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Take", take);

        var list = new List<JobRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            list.Add(Map(reader));

        return list;
    }

    public async Task UpsertAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.JobKey))
            throw new ArgumentException("JobKey is required.", nameof(job));

        const string sql = """
            MERGE dbo.Jobs AS target
            USING (SELECT @JobKey AS JobKey) AS source
            ON target.JobKey = source.JobKey
            WHEN MATCHED THEN UPDATE SET
                JobTitle = @JobTitle,
                Company = @Company,
                Location = @Location,
                Viewed = @Viewed,
                PostedDateText = @PostedDateText,
                PostedDate = @PostedDate,
                IsTopApplicant = @IsTopApplicant,
                ApplyUrl = @ApplyUrl,
                CompensationText = @CompensationText,
                CompSpecified = @CompSpecified,
                ResumeUsed = @ResumeUsed,
                AppliedAt = @AppliedAt,
                RawPaste = COALESCE(@RawPaste, target.RawPaste),
                UpdatedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT
            (
                JobKey, JobTitle, Company, Location, Viewed, PostedDateText, PostedDate,
                IsTopApplicant, ApplyUrl, CompensationText, CompSpecified, ResumeUsed,
                AppliedAt, RawPaste, CreatedAt, UpdatedAt
            )
            VALUES
            (
                @JobKey, @JobTitle, @Company, @Location, @Viewed, @PostedDateText, @PostedDate,
                @IsTopApplicant, @ApplyUrl, @CompensationText, @CompSpecified, @ResumeUsed,
                @AppliedAt, @RawPaste, SYSUTCDATETIME(), SYSUTCDATETIME()
            );
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        AddParameters(command, job);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(SqlCommand command, JobRecord job)
    {
        command.Parameters.AddWithValue("@JobKey", job.JobKey);
        command.Parameters.AddWithValue("@JobTitle", job.JobTitle);
        command.Parameters.AddWithValue("@Company", job.Company);
        command.Parameters.AddWithValue("@Location", (object?)job.Location ?? DBNull.Value);
        command.Parameters.AddWithValue("@Viewed", job.Viewed);
        command.Parameters.AddWithValue("@PostedDateText", (object?)job.PostedDateText ?? DBNull.Value);
        command.Parameters.AddWithValue("@PostedDate", job.PostedDate.HasValue
            ? job.PostedDate.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value);
        command.Parameters.AddWithValue("@IsTopApplicant", job.IsTopApplicant);
        command.Parameters.AddWithValue("@ApplyUrl", (object?)job.ApplyUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@CompensationText", (object?)job.CompensationText ?? DBNull.Value);
        command.Parameters.AddWithValue("@CompSpecified", (object?)job.CompSpecified ?? DBNull.Value);
        command.Parameters.AddWithValue("@ResumeUsed", (object?)job.ResumeUsed ?? DBNull.Value);
        command.Parameters.AddWithValue("@AppliedAt", (object?)job.AppliedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@RawPaste", (object?)job.RawPaste ?? DBNull.Value);
    }

    private static JobRecord Map(SqlDataReader reader)
    {
        return new JobRecord
        {
            JobKey = reader.GetString(0),
            JobTitle = reader.GetString(1),
            Company = reader.GetString(2),
            Location = reader.IsDBNull(3) ? null : reader.GetString(3),
            Viewed = reader.GetBoolean(4),
            PostedDateText = reader.IsDBNull(5) ? null : reader.GetString(5),
            PostedDate = reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
            IsTopApplicant = reader.GetBoolean(7),
            ApplyUrl = reader.IsDBNull(8) ? null : reader.GetString(8),
            CompensationText = reader.IsDBNull(9) ? null : reader.GetString(9),
            CompSpecified = reader.IsDBNull(10) ? null : reader.GetString(10),
            ResumeUsed = reader.IsDBNull(11) ? null : reader.GetString(11),
            AppliedAt = reader.IsDBNull(12) ? null : reader.GetDateTime(12),
            RawPaste = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAt = reader.GetDateTime(14),
            UpdatedAt = reader.GetDateTime(15),
            ExistsInDatabase = true,
        };
    }
}
