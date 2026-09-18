using System.Globalization;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Configuration;
using VisionMaven.Core.Domain;

namespace VisionMaven.Flow.Devices;

/// <summary>把工程配置展平后的设备连接参数视图。</summary>
public sealed class DeviceContext : IDeviceContext
{
    private readonly Dictionary<string, string?> _settings;

    public DeviceContext(DeviceNodeConfig config)
        : this(config.DeviceId, config.Name, config.DriverKey, config.Kind, config.ToSettingMap())
    {
    }

    public DeviceContext(
        string deviceId,
        string name,
        string driverKey,
        DeviceKind kind,
        IReadOnlyDictionary<string, string?> settings)
    {
        DeviceId = deviceId;
        Name = name;
        DriverKey = driverKey;
        Kind = kind;
        _settings = new Dictionary<string, string?>(settings, StringComparer.OrdinalIgnoreCase);
    }

    public string DriverKey { get; }

    public string DeviceId { get; }

    public string Name { get; }

    public DeviceKind Kind { get; }

    public IReadOnlyDictionary<string, string?> Settings => _settings;

    public T? Get<T>(string key)
    {
        if (!_settings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        try
        {
            var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            if (target == typeof(string))
            {
                return (T)(object)value;
            }

            if (target.IsEnum)
            {
                return (T)Enum.Parse(target, value, true);
            }

            return (T)Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return default;
        }
    }

    public string GetString(string key, string fallback)
        => _settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public int GetInt(string key, int fallback)
        => _settings.TryGetValue(key, out var value)
           && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public double GetDouble(string key, double fallback)
        => _settings.TryGetValue(key, out var value)
           && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public bool GetBool(string key, bool fallback)
        => _settings.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    public TimeSpan GetTimeSpan(string key, TimeSpan fallback)
        => _settings.TryGetValue(key, out var value)
           && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? TimeSpan.FromMilliseconds(parsed)
            : fallback;
}
