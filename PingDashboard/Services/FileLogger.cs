namespace PingDashboard.Services;

public interface IFileLogger
{
    /// <summary>Append a timestamped line to the application log file.</summary>
    void Log(string category, string message);
}

/// <summary>
/// Simple thread-safe file logger. Appends one timestamped line per event to a
/// configurable path (FileLog:Path in appsettings). Relative paths resolve
/// against the application base directory.
/// </summary>
public class FileLogger : IFileLogger
{
    private readonly string _path;
    private readonly ILogger<FileLogger> _logger;
    private static readonly object _lock = new();

    public FileLogger(IConfiguration config, ILogger<FileLogger> logger)
    {
        _logger = logger;

        var configured = config["FileLog:Path"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = "logs/pingdashboard.log";

        _path = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(AppContext.BaseDirectory, configured);

        // Ensure the directory exists
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public void Log(string category, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{category}] {message}{Environment.NewLine}";
        try
        {
            lock (_lock)
            {
                File.AppendAllText(_path, line);
            }
        }
        catch (Exception ex)
        {
            // Never let logging break the request — fall back to the app logger
            _logger.LogError(ex, "FileLogger: failed to write to {Path}", _path);
        }
    }
}
