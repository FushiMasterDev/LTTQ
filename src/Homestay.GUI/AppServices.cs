using Homestay.GUI.Configuration;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI;

// Composition root owned by WIN. Bind this read delegate to the real BLL bootstrap service
// once MAC publishes M1.3. GUI never opens SqlConnection or executes SQL.
public sealed class AppServices
{
    public AppSettings Settings { get; }
    public ConnectionStore Connections { get; }
    public FileLogger Logger { get; }
    public Func<ConnectionProfile, CancellationToken, Task<int[]>> ReadSchemaVersionsAsync { get; init; }
        = (_, _) => throw new NotImplementedException();

    private static readonly int[] SupportedVersions = [1, 2, 3, 4, 10, 20];

    public AppServices(AppSettings settings, string connectionPath)
    {
        Settings = settings;
        Connections = new ConnectionStore(connectionPath);
        Logger = new FileLogger(settings.Logging.Folder, settings.Logging.RetainDays);
    }
    public async Task CheckConnectionAsync(ConnectionProfile profile, CancellationToken token)
    {
        var versions = await ReadSchemaVersionsAsync(profile, token);
        if (!versions.Order().SequenceEqual(SupportedVersions))
            throw new InvalidDataException("Phiên bản cơ sở dữ liệu không phù hợp. Vui lòng cập nhật ứng dụng hoặc bộ cài dữ liệu.");
    }
}
