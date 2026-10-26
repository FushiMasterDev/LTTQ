using System.Globalization;

namespace Homestay.GUI.Infrastructure;

// Deliberately records exception type and stack frames, never Message/ToString or input values.
public sealed class FileLogger
{
    private readonly object gate = new();
    private readonly string folder;
    private readonly int retainDays;
    public FileLogger(string folder, int retainDays)
    {
        this.folder = Environment.ExpandEnvironmentVariables(folder);
        this.retainDays = retainDays;
    }
    // Called only with a system maintenance count, never user input or credentials.
    public void Maintenance(int expiredCount)
    {
        try {
            lock(gate) {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"{DateTime.Today:yyyy-MM-dd}.log"),
                    $"{DateTimeOffset.UtcNow:O} Maintenance ExpiredHolds={expiredCount}{Environment.NewLine}");
            }
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { }
    }
    public string Error(Exception exception)
    {
        var id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(folder);
                foreach (var file in Directory.EnumerateFiles(folder, "*.log"))
                {
                    if (DateTime.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                        date < DateTime.Today.AddDays(-retainDays))
                        File.Delete(file);
                }
                var frames = new System.Diagnostics.StackTrace(exception, false).GetFrames() ?? [];
                var locations = frames.Select(f => f.GetMethod()).Where(m => m is not null)
                    .Select(m => m!.DeclaringType?.FullName + "." + m.Name);
                var entry = $"{DateTimeOffset.UtcNow:O} Error {id} Machine={Environment.MachineName} Type={exception.GetType().FullName}"
                    + Environment.NewLine + string.Join(Environment.NewLine, locations) + Environment.NewLine;
                File.AppendAllText(System.IO.Path.Combine(folder, $"{DateTime.Today:yyyy-MM-dd}.log"), entry);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Debug.WriteLine("Không ghi được log kỹ thuật."); }
        return id;
    }
}
