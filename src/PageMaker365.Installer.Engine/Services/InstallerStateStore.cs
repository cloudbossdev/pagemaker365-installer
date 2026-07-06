using System.Text.Json;
using System.Text.Json.Serialization;
using PageMaker365.Installer.Engine.Models;

namespace PageMaker365.Installer.Engine.Services;

public sealed class InstallerStateStore
{
    private const string StateFileName = "session-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootDirectory;

    public InstallerStateStore(string? rootDirectory = null)
    {
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PageMaker365",
                "Installer",
                "sessions")
            : rootDirectory;
    }

    public string RootDirectory => _rootDirectory;

    public static string CreateStateId()
    {
        return $"pm365-session-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..49];
    }

    public string Save(PersistedInstallerState state)
    {
        if (string.IsNullOrWhiteSpace(state.StateId))
        {
            state.StateId = CreateStateId();
        }

        state.SavedAt = DateTimeOffset.UtcNow;
        if (state.IsCompleted && state.CompletedAt is null)
        {
            state.CompletedAt = state.SavedAt;
        }

        var directory = Path.Combine(_rootDirectory, SafePathSegment(state.StateId));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, StateFileName);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    public PersistedInstallerState? LoadMostRecentActive()
    {
        return EnumerateStates()
            .Where(state => !state.IsCompleted)
            .OrderByDescending(state => state.SavedAt)
            .FirstOrDefault();
    }

    public PersistedInstallerState? Load(string stateId)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            return null;
        }

        var path = Path.Combine(_rootDirectory, SafePathSegment(stateId), StateFileName);
        return File.Exists(path) ? LoadFile(path) : null;
    }

    public IReadOnlyList<PersistedInstallerState> EnumerateStates()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(_rootDirectory, StateFileName, SearchOption.AllDirectories)
            .Select(LoadFile)
            .Where(state => state is not null)
            .Select(state => state!)
            .OrderByDescending(state => state.SavedAt)
            .ToList();
    }

    private static PersistedInstallerState? LoadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PersistedInstallerState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string SafePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? CreateStateId() : sanitized;
    }
}
