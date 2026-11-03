using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using TimeInset.Core.Localization;

namespace TimeInset.Core;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    private string? warningKey, warningDetail;
    public string? Warning => warningKey == null ? null : $"{Strings.ResourceManager.GetString(warningKey)}\n{Path}\n{warningDetail}";
    private bool blocked;
    public AppSettings Load()
    {
        warningKey = warningDetail = null; blocked = false;
        try
        {
            string content;
            try { content = File.ReadAllText(Path); }
            catch (FileNotFoundException) { return new(); }
            catch (DirectoryNotFoundException) { return new(); }
            var node = JsonNode.Parse(content) as JsonObject ?? throw new JsonException("Expected an object");
            var clean = new JsonObject();
            var invalid = new List<string>();
            // A wrong type in a single known field must not discard other settings.
            foreach (var property in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
            {
                var pair = node.FirstOrDefault(p => p.Key.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
                if (pair.Key == null) continue;
                try { _ = pair.Value.Deserialize(property.PropertyType, Options); clean[property.Name] = pair.Value?.DeepClone(); }
                catch (Exception ex) when (ex is JsonException or ArgumentException) { invalid.Add(property.Name); }
            }
            var value = clean.Deserialize<AppSettings>(Options)!;
            var normalized = value.Normalize();
            if (invalid.Count > 0 || normalized != value) { warningKey = nameof(Strings.SettingsAdjusted); warningDetail = string.Join(", ", invalid); }
            if (value.SchemaVersion > 1) { blocked = true; warningKey = nameof(Strings.SettingsUnsupported); warningDetail = value.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture); }
            return normalized;
        }
        catch (JsonException ex)
        {
            try { File.Move(Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json")); }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException) { blocked = true; warningKey = nameof(Strings.SettingsUnreadable); warningDetail = move.Message; }
            warningKey ??= nameof(Strings.SettingsCorrupt); warningDetail ??= ex.Message;
            return new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { blocked = true; warningKey = nameof(Strings.SettingsUnreadable); warningDetail = ex.Message; return new(); }
    }
    public void Save(AppSettings settings)
    {
        if (blocked) throw new IOException($"{Path}\n{Warning}");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, settings, Options); stream.Flush(true); }
            File.Move(temp, Path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
