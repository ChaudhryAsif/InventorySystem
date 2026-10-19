using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Data;

public sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Argument(args, "--environment")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";
        var contentRoot = Argument(args, "--content-root") ?? FindWebProject();
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddEnvironmentVariables("DOTNET_")
            .SetBasePath(Path.GetFullPath(contentRoot))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection in the web project's settings or environment before running migrations.");

        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection).Options);
    }

    private static string? Argument(string[] args, string key)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return args[i][(key.Length + 1)..];
        }
        return null;
    }

    private static string FindWebProject()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                foreach (var candidate in new[] { directory.FullName, Path.Combine(directory.FullName, "InventorySystem") })
                {
                    if (File.Exists(Path.Combine(candidate, "InventorySystem.csproj"))
                        && File.Exists(Path.Combine(candidate, "appsettings.json")))
                        return candidate;
                }
            }
        }
        throw new InvalidOperationException("Cannot locate the web project's appsettings.json. Pass -- --content-root <web-project-directory> to the EF command.");
    }
}
