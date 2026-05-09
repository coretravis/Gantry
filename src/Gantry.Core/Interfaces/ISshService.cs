using Gantry.Core.Models;

namespace Gantry.Core.Interfaces;

/// <summary>Manages SSH/SFTP communication with the target server.</summary>
public interface ISshService : IAsyncDisposable
{
    Task ConnectAsync(string host, string username, string keyPath, int port = 22, CancellationToken ct = default);
    Task<CommandResult> ExecuteAsync(string command, TimeSpan? timeout = null, CancellationToken ct = default);
    Task UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default);
    Task UploadStringAsync(string content, string remotePath, CancellationToken ct = default);
    Task<string> DownloadStringAsync(string remotePath, CancellationToken ct = default);
    Task<bool> FileExistsAsync(string remotePath, CancellationToken ct = default);
    Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken ct = default);
    bool IsConnected { get; }
}
