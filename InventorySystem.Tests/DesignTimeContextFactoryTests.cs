using System.Text.Json;
using InventorySystem.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Tests;

public class DesignTimeContextFactoryTests
{
    [Fact]
    public void UsesWebSettingsInsteadOfHardcodedDesignDatabase()
    {
        using var settings = new Settings("FactoryBase");
        using var db = new DesignTimeContextFactory().CreateDbContext(["--content-root", settings.Path, "--environment", "FactoryTest"]);
        Assert.Equal("FactoryBase", new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog);
    }

    [Fact]
    public void EnvironmentSettingsOverrideBaseSettings()
    {
        using var settings = new Settings("FactoryBase");
        settings.Write("appsettings.FactoryTest.json", "FactoryEnvironment");
        using var db = new DesignTimeContextFactory().CreateDbContext(["--content-root=" + settings.Path, "--environment=FactoryTest"]);
        Assert.Equal("FactoryEnvironment", new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog);
    }

    [Fact]
    public void ExplicitConnectionConfigurationOverridesJson()
    {
        using var settings = new Settings("FactoryBase");
        using var db = new DesignTimeContextFactory().CreateDbContext(["--content-root", settings.Path,
            "--ConnectionStrings:DefaultConnection", "Server=(localdb)\\MSSQLLocalDB;Database=FactoryExplicit;Integrated Security=true"]);
        Assert.Equal("FactoryExplicit", new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog);
    }

    [Fact]
    public void MissingConnectionFailsInsteadOfUsingAnotherDatabase()
    {
        using var settings = new Settings("FactoryBase");
        var error = Assert.Throws<InvalidOperationException>(() => new DesignTimeContextFactory()
            .CreateDbContext(["--content-root", settings.Path, "--ConnectionStrings:DefaultConnection="]));
        Assert.Contains("DefaultConnection", error.Message);
    }

    private sealed class Settings : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PosFactoryTest-" + Guid.NewGuid().ToString("N"));
        public Settings(string database) { Directory.CreateDirectory(Path); Write("appsettings.json", database); }
        public void Write(string file, string database) => File.WriteAllText(System.IO.Path.Combine(Path, file),
            JsonSerializer.Serialize(new { ConnectionStrings = new { DefaultConnection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true" } }));
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
