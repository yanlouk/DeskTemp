using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;

namespace DeskTemp.Services;

public sealed class StorageReading
{
    public string Name { get; init; } = "磁盘";
    public string FullName { get; init; } = string.Empty;
    public string Capacity { get; init; } = string.Empty;
    public double SizeGb { get; init; }
    public double? Temperature { get; init; }
    public bool IsSsd { get; init; }
}

/// <summary>
/// Reads physical disk temperatures from the Windows Storage namespace.
/// Requires an elevated process; callers handle access-denied separately.
/// </summary>
public static class StorageReader
{
    public static IReadOnlyList<StorageReading> Read()
    {
        var result = new List<StorageReading>();
        var scope = new ManagementScope("root\\Microsoft\\Windows\\Storage");
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT * FROM MSFT_PhysicalDisk"));
        foreach (ManagementObject disk in searcher.Get())
        {
            string fullName = (disk["FriendlyName"]?.ToString() ?? string.Empty).Trim();
            ushort mediaType = ToUShort(disk["MediaType"]);
            ulong sizeBytes = ToULong(disk["Size"]);
            double sizeGb = sizeBytes / 1_000_000_000d;

            double? temperature = null;
            try
            {
                // MSFT_StorageReliabilityCounter is only reachable as an
                // associated object; the class cannot be enumerated directly.
                foreach (ManagementObject row in disk.GetRelated("MSFT_StorageReliabilityCounter"))
                {
                    double? value = ToDouble(row["Temperature"]);
                    if (value is double temp && temp > 0 && temp <= 120)
                    {
                        temperature = temp;
                        break;
                    }
                }
            }
            catch
            {
                continue;
            }

            if (temperature is not double reading)
                continue;
            if (result.Any(r => r.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new StorageReading
            {
                Name = ShortName(fullName.Length == 0 ? $"磁盘 {disk["DeviceId"]}" : fullName),
                FullName = fullName,
                Capacity = CapacityLabel(sizeBytes),
                SizeGb = sizeGb,
                Temperature = Math.Round(reading, 0),
                IsSsd = mediaType is 4 or 5,
            });
        }

        return result
            .OrderByDescending(disk => disk.IsSsd)
            .ThenBy(disk => disk.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsAccessDenied(Exception ex)
    {
        if (ex is UnauthorizedAccessException || ex.HResult == unchecked((int)0x80070005))
            return true;
        return ex.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("拒绝访问", StringComparison.Ordinal);
    }

    private static string ShortName(string name)
    {
        string result = name.Replace("(R)", string.Empty).Replace("(TM)", string.Empty).Trim();
        result = Regex.Replace(result,
            @"^(?:Samsung\s+SSD|Samsung|WDC|WD|Western\s+Digital|ZHITAI|KIOXIA|Crucial|Kingston|Seagate|Intel|SK\s*hynix|Netac|Lexar|PNY|ADATA|TEAMGROUP|Silicon\s+Power|Hikvision|Colorful|Lenovo|HP|Dell|TOSHIBA)\s+",
            string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\s+\d+(?:\.\d+)?\s*(?:GB|TB)\b.*$", string.Empty,
            RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"-\w{6,}$", string.Empty);
        result = Regex.Replace(result, @"\b(?:SSD|NVMe|SATA)\b", string.Empty, RegexOptions.IgnoreCase);
        result = result.Replace("Plus", string.Empty, StringComparison.OrdinalIgnoreCase);
        result = Regex.Replace(result, @"\s+", string.Empty);

        if (result.Length == 0)
            result = name;
        if (result.Length > 10)
            result = result[..10];
        return result;
    }

    private static string CapacityLabel(ulong bytes)
    {
        if (bytes >= 1_000_000_000_000UL)
            return $"{Math.Max(1, Math.Round(bytes / 1_000_000_000_000d)):0}T";
        if (bytes >= 1_000_000_000UL)
            return $"{Math.Round(bytes / 1_000_000_000d):0}G";
        return string.Empty;
    }

    private static ulong ToULong(object? value)
    {
        try
        {
            return value == null ? 0 : Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static ushort ToUShort(object? value)
    {
        try
        {
            return value == null ? (ushort)0 : Convert.ToUInt16(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
    }

    private static double? ToDouble(object? value)
    {
        try
        {
            return value == null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }
}
