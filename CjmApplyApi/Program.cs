using System.Text.Json;
using CjmApply.Core.Models;
using CjmApply.Core.Parsing;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Environment.GetEnvironmentVariable("CJM_APPLY_DB")
    ?? builder.Configuration["Sqlite:Path"]
    ?? Path.Combine(AppContext.BaseDirectory, "cjm-apply.db");

var apiKey = Environment.GetEnvironmentVariable("CJM_APPLY_API_KEY")
    ?? builder.Configuration["ApiKey"]
    ?? "";

builder.Services.AddSingleton(new SqliteOptions(dbPath, apiKey));

// Browser callers (SPA) only — allow listed origins. Empty list = no CORS
// (non-browser clients with an API key are unaffected).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod();
        if (corsOrigins.Length > 0)
            policy.WithOrigins(corsOrigins);
        else
            policy.SetIsOriginAllowed(_ => false);
    });
});

var app = builder.Build();

EnsureDatabase(dbPath);

if (corsOrigins.Length > 0)
    app.UseCors();

var api = app.MapGroup("/api");

api.MapGet("/health", (SqliteOptions options) => Results.Ok(new
{
    status = "ok",
    dbPath = options.DbPath,
    dbExists = File.Exists(options.DbPath),
}));

api.MapPost("/parse", (ParseRequest request, SqliteOptions options, HttpRequest http) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    var text = request.Text ?? "";
    var source = LinkedInHtmlJobParser.LooksLikeHtml(text) ? "html" : "text";
    var jobs = LinkedInPasteParser.Parse(text);
    var dto = jobs.Select(ToDto).ToList();
    return Results.Ok(new { jobs = dto, count = dto.Count, source });
});

api.MapPost("/jobs/import", async (ImportRequest request, SqliteOptions options, HttpRequest http) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    if (request.Jobs is null || request.Jobs.Count == 0)
        return Results.BadRequest(new { error = "jobs is required" });

    var result = await ImportJobsAsync(options.DbPath, request.Jobs);
    return Results.Ok(result);
});

api.MapGet("/jobs", async (SqliteOptions options, HttpRequest http, int? take) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    var limit = Math.Clamp(take ?? 500, 1, 2000);
    var jobs = await ListJobsAsync(options.DbPath, limit);
    return Results.Ok(new { jobs, count = jobs.Count });
});

api.MapPost("/jobs/{jobKey}/applied/toggle", async (string jobKey, SqliteOptions options, HttpRequest http) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(jobKey))
        return Results.BadRequest(new { error = "jobKey is required" });

    var result = await ToggleFlagAsync(options.DbPath, jobKey, "Applied", "AppliedAt");
    return result is null
        ? Results.NotFound(new { error = "job not found" })
        : Results.Ok(new { jobKey, applied = result.Value, appliedAt = result.At });
});

api.MapPost("/jobs/{jobKey}/ignored/toggle", async (string jobKey, SqliteOptions options, HttpRequest http) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(jobKey))
        return Results.BadRequest(new { error = "jobKey is required" });

    var result = await ToggleFlagAsync(options.DbPath, jobKey, "Ignored", "IgnoredAt");
    return result is null
        ? Results.NotFound(new { error = "job not found" })
        : Results.Ok(new { jobKey, ignored = result.Value, ignoredAt = result.At });
});

api.MapPost("/sql", async (SqlRequest request, SqliteOptions options, HttpRequest http) =>
{
    if (!IsAuthorized(http, options.ApiKey))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Sql))
        return Results.BadRequest(new { error = "sql is required" });

    try
    {
        var result = await ExecuteSqlAsync(options.DbPath, request.Sql, request.Parameters);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

static JobDto ToDto(JobRecord job) => new()
{
    JobKey = job.JobKey,
    JobTitle = job.JobTitle,
    Company = job.Company,
    Location = job.Location,
    Viewed = job.Viewed,
    PostedDateText = job.PostedDateText,
    PostedDate = job.PostedDate?.ToString("yyyy-MM-dd"),
    IsTopApplicant = job.IsTopApplicant,
    ApplyUrl = job.ApplyUrl,
    CompensationText = job.CompensationText,
    CompSpecified = job.CompSpecified,
    ResumeUsed = job.ResumeUsed,
    Applied = job.Applied,
    AppliedAt = job.AppliedAt?.ToString("o"),
    Ignored = job.Ignored,
    IgnoredAt = job.IgnoredAt?.ToString("o"),
    CreatedAt = job.CreatedAt == default ? null : job.CreatedAt.ToString("o"),
    RawPaste = job.RawPaste,
};

static async Task<List<JobDto>> ListJobsAsync(string dbPath, int take)
{
    await using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT JobKey, JobTitle, Company, Location, Viewed, PostedDateText, PostedDate,
               IsTopApplicant, ApplyUrl, CompensationText, CompSpecified, ResumeUsed,
               Applied, AppliedAt, Ignored, IgnoredAt, RawPaste, CreatedAt, UpdatedAt
        FROM Jobs
        ORDER BY datetime(UpdatedAt) DESC
        LIMIT @Take;
        """;
    command.Parameters.AddWithValue("@Take", take);

    var list = new List<JobDto>();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        list.Add(MapJobDto(reader));

    return list;
}

static async Task<FlagToggleResult?> ToggleFlagAsync(
    string dbPath,
    string jobKey,
    string flagColumn,
    string atColumn)
{
    // Column names are internal constants only (Applied/Ignored) — never from request input.
    await using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    await using var read = connection.CreateCommand();
    read.CommandText = $"SELECT {flagColumn} FROM Jobs WHERE JobKey = @JobKey;";
    read.Parameters.AddWithValue("@JobKey", jobKey);
    var existing = await read.ExecuteScalarAsync();
    if (existing is null || existing is DBNull)
        return null;

    var next = Convert.ToInt64(existing) == 0;
    var now = DateTime.UtcNow.ToString("o");

    await using var write = connection.CreateCommand();
    write.CommandText = $"""
        UPDATE Jobs
        SET {flagColumn} = @Flag,
            {atColumn} = @At,
            UpdatedAt = @UpdatedAt
        WHERE JobKey = @JobKey;
        """;
    write.Parameters.AddWithValue("@Flag", next ? 1 : 0);
    write.Parameters.AddWithValue("@At", next ? now : DBNull.Value);
    write.Parameters.AddWithValue("@UpdatedAt", now);
    write.Parameters.AddWithValue("@JobKey", jobKey);
    await write.ExecuteNonQueryAsync();

    return new FlagToggleResult(next, next ? now : null);
}

static JobDto MapJobDto(SqliteDataReader reader)
{
    DateTime? ParseDto(object? value)
    {
        if (value is null || value is DBNull) return null;
        return DateTime.TryParse(Convert.ToString(value), out var dt) ? dt : null;
    }

    bool ReadBool(string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && Convert.ToInt64(reader.GetValue(ordinal)) != 0;
    }

    return new JobDto
    {
        JobKey = reader["JobKey"]?.ToString(),
        JobTitle = reader["JobTitle"]?.ToString(),
        Company = reader["Company"]?.ToString(),
        Location = reader["Location"] is DBNull ? null : reader["Location"]?.ToString(),
        Viewed = Convert.ToInt64(reader["Viewed"]) != 0,
        PostedDateText = reader["PostedDateText"] is DBNull ? null : reader["PostedDateText"]?.ToString(),
        PostedDate = reader["PostedDate"] is DBNull ? null : reader["PostedDate"]?.ToString(),
        IsTopApplicant = Convert.ToInt64(reader["IsTopApplicant"]) != 0,
        ApplyUrl = reader["ApplyUrl"] is DBNull ? null : reader["ApplyUrl"]?.ToString(),
        CompensationText = reader["CompensationText"] is DBNull ? null : reader["CompensationText"]?.ToString(),
        CompSpecified = reader["CompSpecified"] is DBNull ? null : reader["CompSpecified"]?.ToString(),
        ResumeUsed = reader["ResumeUsed"] is DBNull ? null : reader["ResumeUsed"]?.ToString(),
        Applied = ReadBool("Applied"),
        AppliedAt = ParseDto(reader["AppliedAt"])?.ToString("o"),
        Ignored = ReadBool("Ignored"),
        IgnoredAt = ParseDto(reader["IgnoredAt"])?.ToString("o"),
        CreatedAt = ParseDto(reader["CreatedAt"])?.ToString("o"),
        RawPaste = reader["RawPaste"] is DBNull ? null : reader["RawPaste"]?.ToString(),
    };
}

static async Task<object> ImportJobsAsync(string dbPath, List<JobDto?> jobs)
{
    var added = 0;
    var skipped = 0;
    var errors = new List<object>();

    await using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    await using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync();
    }

    foreach (var job in jobs)
    {
        if (job is null)
        {
            errors.Add(new { error = "Job entry was null" });
            continue;
        }

        var title = job.JobTitle?.Trim() ?? "";
        var company = job.Company?.Trim() ?? "";
        var location = job.Location?.Trim();
        var jobKey = string.IsNullOrWhiteSpace(job.JobKey)
            ? JobKeyBuilder.Build(title, company, location)
            : job.JobKey.Trim();

        if (string.IsNullOrWhiteSpace(jobKey) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(company))
        {
            errors.Add(new { jobKey, error = "JobTitle and Company are required" });
            continue;
        }

        // Recompute key from editable fields so preview edits stick.
        jobKey = JobKeyBuilder.Build(title, company, location);
        if (string.IsNullOrWhiteSpace(jobKey))
        {
            errors.Add(new { jobTitle = title, error = "Could not build JobKey" });
            continue;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO Jobs (
                    JobKey, JobTitle, Company, Location, Viewed, PostedDateText, PostedDate,
                    IsTopApplicant, ApplyUrl, CompensationText, CompSpecified, ResumeUsed,
                    Applied, AppliedAt, Ignored, IgnoredAt, RawPaste, CreatedAt, UpdatedAt
                ) VALUES (
                    @JobKey, @JobTitle, @Company, @Location, @Viewed, @PostedDateText, @PostedDate,
                    @IsTopApplicant, @ApplyUrl, @CompensationText, @CompSpecified, @ResumeUsed,
                    @Applied, @AppliedAt, @Ignored, @IgnoredAt, @RawPaste, @Now, @Now
                );
                """;

            var now = DateTime.UtcNow.ToString("o");
            var applied = job.Applied || !string.IsNullOrWhiteSpace(job.AppliedAt);
            var ignored = job.Ignored || !string.IsNullOrWhiteSpace(job.IgnoredAt);
            command.Parameters.AddWithValue("@JobKey", jobKey);
            command.Parameters.AddWithValue("@JobTitle", title);
            command.Parameters.AddWithValue("@Company", company);
            command.Parameters.AddWithValue("@Location", (object?)location ?? DBNull.Value);
            command.Parameters.AddWithValue("@Viewed", job.Viewed ? 1 : 0);
            command.Parameters.AddWithValue("@PostedDateText", (object?)job.PostedDateText ?? DBNull.Value);
            command.Parameters.AddWithValue("@PostedDate", (object?)job.PostedDate ?? DBNull.Value);
            command.Parameters.AddWithValue("@IsTopApplicant", job.IsTopApplicant ? 1 : 0);
            command.Parameters.AddWithValue("@ApplyUrl", (object?)job.ApplyUrl ?? DBNull.Value);
            command.Parameters.AddWithValue("@CompensationText", (object?)job.CompensationText ?? DBNull.Value);
            command.Parameters.AddWithValue("@CompSpecified", (object?)job.CompSpecified ?? DBNull.Value);
            command.Parameters.AddWithValue("@ResumeUsed", (object?)job.ResumeUsed ?? DBNull.Value);
            command.Parameters.AddWithValue("@Applied", applied ? 1 : 0);
            command.Parameters.AddWithValue("@AppliedAt", applied
                ? (object?)(string.IsNullOrWhiteSpace(job.AppliedAt) ? now : job.AppliedAt)
                : DBNull.Value);
            command.Parameters.AddWithValue("@Ignored", ignored ? 1 : 0);
            command.Parameters.AddWithValue("@IgnoredAt", ignored
                ? (object?)(string.IsNullOrWhiteSpace(job.IgnoredAt) ? now : job.IgnoredAt)
                : DBNull.Value);
            command.Parameters.AddWithValue("@RawPaste", (object?)job.RawPaste ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", now);

            var affected = await command.ExecuteNonQueryAsync();
            if (affected > 0)
                added++;
            else
                skipped++;
        }
        catch (Exception ex)
        {
            // PK / constraint failures count as skipped so first-write wins.
            if (ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
            }
            else
            {
                errors.Add(new { jobKey, error = ex.Message });
            }
        }
    }

    return new { added, skipped, errors, total = jobs.Count };
}

static bool IsAuthorized(HttpRequest http, string expectedKey)
{
    // Empty key = open (local only). Production should set CJM_APPLY_API_KEY.
    if (string.IsNullOrWhiteSpace(expectedKey))
        return true;

    if (http.Headers.TryGetValue("X-Api-Key", out var header) &&
        string.Equals(header.ToString(), expectedKey, StringComparison.Ordinal))
        return true;

    var auth = http.Headers.Authorization.ToString();
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(auth["Bearer ".Length..].Trim(), expectedKey, StringComparison.Ordinal))
        return true;

    return false;
}

static void EnsureDatabase(string dbPath)
{
    var dir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrWhiteSpace(dir))
        Directory.CreateDirectory(dir);

    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();

    using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
    }

    using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE IF NOT EXISTS Jobs (
            JobKey            TEXT NOT NULL PRIMARY KEY,
            JobTitle          TEXT NOT NULL,
            Company           TEXT NOT NULL,
            Location          TEXT NULL,
            Viewed            INTEGER NOT NULL DEFAULT 0,
            PostedDateText    TEXT NULL,
            PostedDate        TEXT NULL,
            IsTopApplicant    INTEGER NOT NULL DEFAULT 0,
            ApplyUrl          TEXT NULL,
            CompensationText  TEXT NULL,
            CompSpecified     TEXT NULL,
            ResumeUsed        TEXT NULL,
            Applied           INTEGER NOT NULL DEFAULT 0,
            AppliedAt         TEXT NULL,
            Ignored           INTEGER NOT NULL DEFAULT 0,
            IgnoredAt         TEXT NULL,
            RawPaste          TEXT NULL,
            CreatedAt         TEXT NOT NULL,
            UpdatedAt         TEXT NOT NULL
        );
        """;
    command.ExecuteNonQuery();

    EnsureColumn(connection, "Applied", "INTEGER NOT NULL DEFAULT 0");
    EnsureColumn(connection, "Ignored", "INTEGER NOT NULL DEFAULT 0");
    EnsureColumn(connection, "IgnoredAt", "TEXT NULL");
}

static void EnsureColumn(SqliteConnection connection, string columnName, string columnDef)
{
    using var check = connection.CreateCommand();
    check.CommandText = "PRAGMA table_info(Jobs);";
    using var reader = check.ExecuteReader();
    while (reader.Read())
    {
        if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            return;
    }

    reader.Close();
    using var alter = connection.CreateCommand();
    alter.CommandText = $"ALTER TABLE Jobs ADD COLUMN {columnName} {columnDef};";
    alter.ExecuteNonQuery();
}

static async Task<object> ExecuteSqlAsync(
    string dbPath,
    string sql,
    Dictionary<string, JsonElement>? parameters)
{
    await using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();

    await using (var pragma = connection.CreateCommand())
    {
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync();
    }

    // Allow multiple statements separated by semicolons; last result wins for response shape.
    var batches = SplitStatements(sql);
    object? last = null;

    foreach (var statement in batches)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = statement;
        BindParameters(command, parameters);

        if (IsQuery(statement))
        {
            await using var reader = await command.ExecuteReaderAsync();
            last = await ReadResultAsync(reader);
        }
        else
        {
            var affected = await command.ExecuteNonQueryAsync();
            last = new { rowsAffected = affected };
        }
    }

    return last ?? new { rowsAffected = 0 };
}

static void BindParameters(SqliteCommand command, Dictionary<string, JsonElement>? parameters)
{
    if (parameters is null || parameters.Count == 0)
        return;

    foreach (var (name, value) in parameters)
    {
        var paramName = name.StartsWith('@') ? name : "@" + name;
        command.Parameters.AddWithValue(paramName, ToClr(value));
    }
}

static object ToClr(JsonElement value) => value.ValueKind switch
{
    JsonValueKind.Null => DBNull.Value,
    JsonValueKind.String => value.GetString() ?? "",
    JsonValueKind.True => 1,
    JsonValueKind.False => 0,
    JsonValueKind.Number when value.TryGetInt64(out var l) => l,
    JsonValueKind.Number when value.TryGetDouble(out var d) => d,
    _ => value.ToString(),
};

static async Task<object> ReadResultAsync(SqliteDataReader reader)
{
    var columns = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName)
        .ToArray();

    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
            row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        rows.Add(row);
    }

    return new { columns, rows, rowCount = rows.Count };
}

static bool IsQuery(string sql)
{
    var trimmed = sql.TrimStart();
    return StartsWithKeyword(trimmed, "SELECT")
        || StartsWithKeyword(trimmed, "WITH")
        || StartsWithKeyword(trimmed, "PRAGMA")
        || StartsWithKeyword(trimmed, "EXPLAIN");
}

static bool StartsWithKeyword(string sql, string keyword) =>
    sql.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)
    && (sql.Length == keyword.Length || !char.IsLetterOrDigit(sql[keyword.Length]));

static List<string> SplitStatements(string sql)
{
    // Simple split on semicolons outside quotes — enough for relay use.
    var list = new List<string>();
    var current = new System.Text.StringBuilder();
    var inSingle = false;
    var inDouble = false;

    for (var i = 0; i < sql.Length; i++)
    {
        var c = sql[i];
        if (c == '\'' && !inDouble)
            inSingle = !inSingle;
        else if (c == '"' && !inSingle)
            inDouble = !inDouble;

        if (c == ';' && !inSingle && !inDouble)
        {
            var piece = current.ToString().Trim();
            if (piece.Length > 0)
                list.Add(piece);
            current.Clear();
            continue;
        }

        current.Append(c);
    }

    var last = current.ToString().Trim();
    if (last.Length > 0)
        list.Add(last);

    return list.Count > 0 ? list : [sql.Trim()];
}

internal sealed record FlagToggleResult(bool Value, string? At);

internal sealed record SqliteOptions(string DbPath, string ApiKey);

internal sealed class SqlRequest
{
    public string Sql { get; set; } = "";
    public Dictionary<string, JsonElement>? Parameters { get; set; }
}

internal sealed class ParseRequest
{
    public string? Text { get; set; }
}

internal sealed class ImportRequest
{
    public List<JobDto?>? Jobs { get; set; }
}

internal sealed class JobDto
{
    public string? JobKey { get; set; }
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string? Location { get; set; }
    public bool Viewed { get; set; }
    public string? PostedDateText { get; set; }
    public string? PostedDate { get; set; }
    public bool IsTopApplicant { get; set; }
    public string? ApplyUrl { get; set; }
    public string? CompensationText { get; set; }
    public string? CompSpecified { get; set; }
    public string? ResumeUsed { get; set; }
    public bool Applied { get; set; }
    public string? AppliedAt { get; set; }
    public bool Ignored { get; set; }
    public string? IgnoredAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? RawPaste { get; set; }
}
