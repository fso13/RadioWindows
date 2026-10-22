using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RadioWindows.Core;

/// <summary>
/// Persisted key/value preferences in one JSON file. Keys mirror the macOS app
/// (and the older Flutter build, whose entries were written with a
/// <c>flutter.</c> prefix), so a settings file can be moved between platforms.
/// </summary>
public sealed class Preferences : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Timer _flushTimer;
    private readonly Dictionary<string, JsonNode?> _values = new(StringComparer.Ordinal);
    private bool _dirty;
    private bool _disposed;

    public Preferences(string path)
    {
        _path = path;
        Load();
        _flushTimer = new Timer(_ => Flush(), null, 1_000, 1_000);
    }

    public Preferences(AppPaths paths)
        : this(paths.SettingsFile)
    {
    }

    public string FilePath => _path;

    /// <summary>Copies the settings written by the Flutter build once.</summary>
    public void MigrateFlutterKeysIfNeeded()
    {
        if (GetBoolLike("native_migration_v1") == true)
        {
            return;
        }

        string[] keys =
        [
            "volume", "resume_last_station", "last_station_id", "auto_start", "theme_preference",
            "eq_enabled", "eq_preset", "eq_gains", "eq_custom_gains",
        ];

        foreach (var key in keys)
        {
            var source = "flutter." + key;
            if (!Has(key) && Has(source))
            {
                Set(key, Get(source)?.DeepClone());
            }
        }

        var stations = Get("flutter.stations_v1");
        if (!Has("stations_v1") && stations is not null)
        {
            Set("stations_v1", stations.DeepClone());
        }

        Set("native_migration_v1", true);
        Flush();
    }

    public bool Has(string key)
    {
        lock (_gate)
        {
            return _values.ContainsKey(key);
        }
    }

    public JsonNode? Get(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public string? GetString(string key)
    {
        var node = Get(key);
        return node switch
        {
            null => null,
            JsonValue value when value.TryGetValue<string>(out var text) => text,
            JsonValue value => value.ToJsonString(),
            _ => node.ToJsonString(),
        };
    }

    public double? GetDoubleLike(string key)
    {
        var node = Get(key);
        return node switch
        {
            JsonValue value when value.TryGetValue<double>(out var number) => number,
            JsonValue value when value.TryGetValue<string>(out var text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public int? GetIntLike(string key)
    {
        var value = GetDoubleLike(key);
        return value is null ? null : (int)Math.Round(value.Value);
    }

    public bool? GetBoolLike(string key)
    {
        var node = Get(key);
        switch (node)
        {
            case null:
                return null;
            case JsonValue value when value.TryGetValue<bool>(out var flag):
                return flag;
            case JsonValue value when value.TryGetValue<double>(out var number):
                return number != 0;
            case JsonValue value when value.TryGetValue<string>(out var text):
                return text.ToLowerInvariant() switch
                {
                    "1" or "true" or "yes" => true,
                    "0" or "false" or "no" => false,
                    _ => null,
                };
            default:
                return null;
        }
    }

    /// <summary>Reads a JSON array, tolerating the JSON-encoded string Flutter used.</summary>
    public JsonArray? GetArray(string key)
    {
        var node = Get(key);
        if (node is JsonArray array)
        {
            return array;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                return JsonNode.Parse(text) as JsonArray;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    public double[]? GetDoubleArray(string key)
    {
        var array = GetArray(key);
        if (array is null || array.Count == 0)
        {
            return null;
        }

        var result = new List<double>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<double>(out var number))
            {
                result.Add(number);
            }
            else if (item is JsonValue textValue && textValue.TryGetValue<string>(out var text)
                     && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result.Count == 0 ? null : result.ToArray();
    }

    public void Set(string key, JsonNode? value)
    {
        lock (_gate)
        {
            _values[key] = value;
            _dirty = true;
        }
    }

    public void Set(string key, string? value)
    {
        if (value is null)
        {
            Remove(key);
            return;
        }

        Set(key, JsonValue.Create(value));
    }

    public void Set(string key, double value) => Set(key, JsonValue.Create(value));

    public void Set(string key, bool value) => Set(key, JsonValue.Create(value));

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_values.Remove(key))
            {
                _dirty = true;
            }
        }
    }

    public void Flush()
    {
        JsonObject snapshot;
        lock (_gate)
        {
            if (!_dirty || _disposed)
            {
                return;
            }

            snapshot = new JsonObject();
            foreach (var pair in _values)
            {
                snapshot[pair.Key] = pair.Value?.DeepClone();
            }

            _dirty = false;
        }

        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, snapshot.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, true);
        }
        catch (IOException)
        {
            // A settings write must never crash playback; the next tick retries.
            lock (_gate)
            {
                _dirty = true;
            }
        }
        catch (UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_gate)
        {
            _disposed = true;
        }

        _flushTimer.Dispose();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var text = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(text) || JsonNode.Parse(text) is not JsonObject root)
            {
                return;
            }

            foreach (var pair in root)
            {
                _values[pair.Key] = pair.Value?.DeepClone();
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupted file is replaced on the next flush; defaults stay in effect.
        }
    }
}
