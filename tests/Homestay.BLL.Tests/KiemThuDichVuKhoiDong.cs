using Homestay.BLL.Services;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử đọc phiên bản schema CSDL lúc khởi động.</summary>
public sealed class StartupServiceTests
{
    [SqlFact]
    public async Task ReadsInstalledSchemaVersionsWithoutSession()
    {
        var versions = await new StartupService(SqlFixture.Factory()).GetSchemaVersionsAsync(SqlFixture.Token);
        Assert.Equal(versions.Order(), versions);
        Assert.Subset(new HashSet<int>(versions), new HashSet<int> { 1, 2, 3, 4, 10 });
    }
}
