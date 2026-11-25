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

    var jobs = LinkedInPasteParser.Parse(request.Text ?? "");
    var dto = jobs.Select(ToDto).ToList();
    return Results.Ok(new { jobs = dto, count = dto.Count });
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
    AppliedAt = job.AppliedAt?.ToString("o"),
    RawPaste = job.RawPaste,
};

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
                    AppliedAt, RawPaste, CreatedAt, UpdatedAt
                ) VALUES (
                    @JobKey, @JobTitle, @Company, @Location, @Viewed, @PostedDateText, @PostedDate,
                    @IsTopApplicant, @ApplyUrl, @CompensationText, @CompSpecified, @ResumeUsed,
                    @AppliedAt, @RawPaste, @Now, @Now
                );
                """;

            var now = DateTime.UtcNow.ToString("o");
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
            command.Parameters.AddWithValue("@AppliedAt", (object?)job.AppliedAt ?? DBNull.Value);
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
            AppliedAt         TEXT NULL,
            RawPaste          TEXT NULL,
            CreatedAt         TEXT NOT NULL,
            UpdatedAt         TEXT NOT NULL
        );
        """;
    command.ExecuteNonQuery();
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
    public string? AppliedAt { get; set; }
    public string? RawPaste { get; set; }
}
