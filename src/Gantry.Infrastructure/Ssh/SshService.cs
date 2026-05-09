using System.Diagnostics;
using Gantry.Core.Exceptions;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace Gantry.Infrastructure.Ssh;

public class SshService : ISshService
{
    private readonly ILogger<SshService> _logger;
    private SshClient? _ssh;
    private SftpClient? _sftp;

    private const int MaxRetries = 3;
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(60);

    public SshService(ILogger<SshService> logger) => _logger = logger;

    public bool IsConnected => _ssh?.IsConnected == true;

    public async Task ConnectAsync(string host, string username, string keyPath, int port = 22, CancellationToken ct = default)
    {
        _logger.LogDebug("Connecting to {Host}:{Port} as {User} using key {Key}", host, port, username, keyPath);

        var expandedPath = keyPath.Replace("~", System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile));
        if (!File.Exists(expandedPath))
            throw new GantryException($"SSH key not found at '{expandedPath}'.", $"Ensure the key file exists or specify a different path.");

        var keyFile = new PrivateKeyFile(expandedPath);
        var authMethod = new PrivateKeyAuthenticationMethod(username, keyFile);
        var connectionInfo = new ConnectionInfo(host, port, username, authMethod)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        Exception? lastEx = null;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                _ssh = new SshClient(connectionInfo);
                _sftp = new SftpClient(connectionInfo);
                await Task.Run(() => { _ssh.Connect(); _sftp.Connect(); }, ct);
                _logger.LogInformation("SSH connection established to {Host}:{Port}", host, port);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger.LogWarning("Connection attempt {Attempt}/{Max} failed: {Error}", attempt, MaxRetries, ex.Message);
                if (attempt < MaxRetries)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct);
            }
        }

        throw new GantryException($"Failed to connect to {host}:{port} after {MaxRetries} attempts: {lastEx?.Message}",
            lastEx!, "Verify the host is reachable, the SSH key is correct, and port 22 is open.");
    }

    public async Task<CommandResult> ExecuteAsync(string command, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        EnsureConnected();
        var sw = Stopwatch.StartNew();

        _logger.LogDebug("SSH execute: {Command}", command);

        using var cmd = _ssh!.CreateCommand(command);
        cmd.CommandTimeout = timeout ?? DefaultCommandTimeout;

        var result = await Task.Run(() => cmd.Execute(), ct);
        sw.Stop();

        var commandResult = new CommandResult
        {
            ExitCode = cmd.ExitStatus ?? -1,
            Stdout = cmd.Result?.TrimEnd() ?? string.Empty,
            Stderr = cmd.Error?.TrimEnd() ?? string.Empty,
            Duration = sw.Elapsed,
            Command = command
        };

        _logger.LogDebug("SSH exit {ExitCode} ({Duration}ms): {Command}", commandResult.ExitCode, sw.ElapsedMilliseconds, command);

        if (!commandResult.Success && !string.IsNullOrWhiteSpace(commandResult.Stderr))
            _logger.LogDebug("Stderr: {Stderr}", commandResult.Stderr);

        return commandResult;
    }

    public async Task UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default)
    {
        EnsureSftpConnected();
        _logger.LogDebug("Uploading {Local} → {Remote}", localPath, remotePath);

        await using var stream = File.OpenRead(localPath);
        await Task.Run(() => _sftp!.UploadFile(stream, remotePath, true), ct);

        var localSize = new FileInfo(localPath).Length;
        var remoteSize = await Task.Run(() => _sftp!.GetAttributes(remotePath).Size, ct);
        if (localSize != remoteSize)
            throw new GantryException($"File transfer verification failed for {remotePath}. Expected {localSize} bytes, got {remoteSize}.");

        _logger.LogDebug("Upload complete: {Remote} ({Bytes} bytes)", remotePath, remoteSize);
    }

    public async Task UploadStringAsync(string content, string remotePath, CancellationToken ct = default)
    {
        EnsureSftpConnected();
        _logger.LogDebug("Uploading string content to {Remote}", remotePath);

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await Task.Run(() => _sftp!.UploadFile(stream, remotePath, true), ct);
    }

    public async Task<string> DownloadStringAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureSftpConnected();
        using var stream = new MemoryStream();
        await Task.Run(() => _sftp!.DownloadFile(remotePath, stream), ct);
        stream.Position = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<bool> FileExistsAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureSftpConnected();
        return await Task.Run(() => _sftp!.Exists(remotePath), ct);
    }

    public async Task<bool> DirectoryExistsAsync(string remotePath, CancellationToken ct = default)
    {
        EnsureSftpConnected();
        return await Task.Run(() =>
        {
            try { return _sftp!.GetAttributes(remotePath).IsDirectory; }
            catch { return false; }
        }, ct);
    }

    private void EnsureConnected()
    {
        if (_ssh == null || !_ssh.IsConnected)
            throw new GantryException("SSH client is not connected. Call ConnectAsync first.");
    }

    private void EnsureSftpConnected()
    {
        if (_sftp == null || !_sftp.IsConnected)
            throw new GantryException("SFTP client is not connected. Call ConnectAsync first.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_sftp?.IsConnected == true)
        {
            await Task.Run(() => _sftp.Disconnect());
            _sftp.Dispose();
        }
        if (_ssh?.IsConnected == true)
        {
            await Task.Run(() => _ssh.Disconnect());
            _ssh.Dispose();
        }
    }
}
