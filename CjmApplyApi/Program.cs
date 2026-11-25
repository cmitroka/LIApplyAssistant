using System.Text.Json;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Environment.GetEnvironmentVariable("CJM_APPLY_DB")
    ?? builder.Configuration["Sqlite:Path"]
    ?? Path.Combine(AppContext.BaseDirectory, "cjm-apply.db");

var apiKey = Environment.GetEnvironmentVariable("CJM_APPLY_API_KEY")
    ?? builder.Configuration["ApiKey"]
    ?? "";

builder.Services.AddSingleton(new SqliteOptions(dbPath, apiKey));

var app = builder.Build();

EnsureDatabase(dbPath);

app.MapGet("/health", (SqliteOptions options) => Results.Ok(new
{
    status = "ok",
    dbPath = options.DbPath,
    dbExists = File.Exists(options.DbPath),
}));

app.MapPost("/sql", async (SqlRequest request, SqliteOptions options, HttpRequest http) =>
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
