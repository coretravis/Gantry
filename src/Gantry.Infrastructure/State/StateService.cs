using System.Text.Json;
using Gantry.Core.Interfaces;
using Gantry.Core.Models;

namespace Gantry.Infrastructure.State;

public class StateService : IStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static string StatePath(string appName) => $"/var/www/{appName}/gantry.json";

    public async Task<GantryState> ReadAsync(ISshService ssh, string appName, CancellationToken ct = default)
    {
        var path = StatePath(appName);
        if (!await ssh.FileExistsAsync(path, ct))
            return new GantryState { GantryVersion = "1.0.0" };

        var json = await ssh.DownloadStringAsync(path, ct);
        return JsonSerializer.Deserialize<GantryState>(json, JsonOptions) ?? new GantryState();
    }

    public async Task WriteAsync(ISshService ssh, string appName, GantryState state, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await ssh.UploadStringAsync(json, StatePath(appName), ct);
    }
}
