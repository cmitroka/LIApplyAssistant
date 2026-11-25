using Microsoft.Extensions.Configuration;

namespace CjmApplyAssistant;

public static class AppConfig
{
    public static IConfiguration Load()
    {
        var basePath = AppContext.BaseDirectory;
        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
    }

    public static string? GetConnectionString(IConfiguration configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("CJM_APPLY_SQL");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv.Trim();

        return configuration.GetConnectionString("AzureSql");
    }
}
