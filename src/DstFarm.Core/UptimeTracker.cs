using System.Text.Json;
using System.Text.Json.Serialization;

namespace DstFarm.Core;

public sealed class UptimeStats
{
    [JsonPropertyName("totalSeconds")]
    public double TotalSeconds { get; set; }

    [JsonPropertyName("sessions")]
    public int Sessions { get; set; }

    [JsonPropertyName("lastStop")]
    public DateTimeOffset? LastStop { get; set; }

    [JsonIgnore]
    public TimeSpan Total => TimeSpan.FromSeconds(TotalSeconds);
}

/// <summary>Копит суммарный аптайм — по нему видно, сколько часов реально накручено.</summary>
public sealed class UptimeTracker(FarmConfig config)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly FarmConfig config = config ?? throw new ArgumentNullException(nameof(config));

    private string File_ => Path.Combine(config.StatePath, "uptime.json");

    public UptimeStats Read()
    {
        if (!File.Exists(File_))
            return new UptimeStats();
        try
        {
            return JsonSerializer.Deserialize<UptimeStats>(File.ReadAllText(File_), Options) ?? new UptimeStats();
        }
        catch (JsonException)
        {
            return new UptimeStats();
        }
    }

    public void Add(TimeSpan elapsed)
    {
        var stats = Read();
        stats.TotalSeconds = Math.Round(stats.TotalSeconds + elapsed.TotalSeconds, 1);
        stats.Sessions++;
        stats.LastStop = DateTimeOffset.Now;
        Directory.CreateDirectory(config.StatePath);
        File.WriteAllText(File_, JsonSerializer.Serialize(stats, Options));
    }
}
