using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace DeskTemp.Services;

public sealed class FanReading
{
    public string Label { get; init; } = "风扇";
    public string SensorName { get; init; } = string.Empty;
    public string HardwareName { get; init; } = string.Empty;
    public double Rpm { get; init; }
    public bool IsGpu { get; init; }
}

public sealed class SensorSnapshot
{
    public double? CpuTemp { get; init; }
    public double? CpuLoad { get; init; }
    public double? CpuClockMhz { get; init; }
    public string CpuName { get; init; } = "CPU";
    public double? MemoryLoad { get; init; }
    public double? MemoryUsedGb { get; init; }
    public double? MemoryTotalGb { get; init; }

    public double? GpuTemp { get; init; }
    public double? GpuLoad { get; init; }
    public double? GpuPower { get; init; }
    public double? GpuMemoryUsedMb { get; init; }
    public double? GpuMemoryTotalMb { get; init; }
    public string GpuName { get; init; } = "GPU";
    public IReadOnlyList<FanReading> Fans { get; init; } = Array.Empty<FanReading>();
    public IReadOnlyList<StorageReading> Disks { get; init; } = Array.Empty<StorageReading>();
    public bool StorageAccessDenied { get; init; }
    public bool StorageLoaded { get; init; }

    public bool CpuPresent { get; init; }
    public bool GpuPresent { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Owns LibreHardwareMonitor on a dedicated background thread and raises one snapshot per second.
/// </summary>
public sealed class SensorService : IDisposable
{
    private readonly bool _preview;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _seenBoardFans = new();
    private readonly object _storageLock = new();
    private Thread? _thread;
    private volatile SensorSnapshot? _latest;
    private volatile bool _storageEnabled;
    private volatile bool _paused;
    private IReadOnlyList<StorageReading> _storageCache = Array.Empty<StorageReading>();
    private bool _storageAccessDenied;
    private long _storageFetchedAt = -100000;

    public SensorService(bool preview) => _preview = preview;

    public SensorSnapshot? Latest => _latest;

    public bool StorageEnabled
    {
        get => _storageEnabled;
        set => _storageEnabled = value;
    }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public void InvalidateStorage()
    {
        lock (_storageLock)
        {
            _storageFetchedAt = -100000;
        }
    }

    public event Action<SensorSnapshot>? Updated;

    public void Start()
    {
        if (_preview)
        {
            _thread = new Thread(PreviewLoop) { IsBackground = true, Name = "DeskTempPreview" };
        }
        else
        {
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "DeskTempSensors" };
        }
        _thread.Start();
    }

    private void PreviewLoop()
    {
        long start = Environment.TickCount64;
        var random = new Random();
        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                if (_cts.Token.WaitHandle.WaitOne(500))
                    return;
                continue;
            }

            double t = (Environment.TickCount64 - start) / 1000.0;
            var snapshot = new SensorSnapshot
            {
                CpuPresent = true,
                GpuPresent = true,
                CpuName = "i5-12400F",
                GpuName = "RTX 3060",
                CpuTemp = Math.Round(47 + 6 * Math.Sin(t / 9.0) + random.NextDouble() * 1.4, 1),
                CpuLoad = Math.Round(22 + 15 * Math.Sin(t / 5.0) + random.NextDouble() * 5, 0),
                CpuClockMhz = 3990 + random.Next(-160, 160),
                MemoryLoad = 42,
                MemoryUsedGb = 13.4,
                MemoryTotalGb = 32,
                GpuTemp = Math.Round(53 + 8 * Math.Sin(t / 13.0 + 1.2) + random.NextDouble() * 1.2, 1),
                GpuLoad = Math.Round(28 + 25 * Math.Sin(t / 7.0 + 0.6) + random.NextDouble() * 6, 0),
                GpuPower = Math.Round(38 + 26 * Math.Sin(t / 6.0) + random.NextDouble() * 5, 0),
                GpuMemoryUsedMb = 3600,
                GpuMemoryTotalMb = 12288,
                Fans = new[]
                {
                    new FanReading { Label = "主板1", SensorName = "Fan #1", HardwareName = "Preview", Rpm = 693 },
                    new FanReading { Label = "主板2", SensorName = "Fan #2", HardwareName = "Preview", Rpm = 5769 },
                    new FanReading { Label = "显卡", SensorName = "GPU Fan 1 0 / GPU Fan 2 0", HardwareName = "Preview", Rpm = 0, IsGpu = true },
                },
                StorageLoaded = true,
                Disks = new[]
                {
                    new StorageReading { Name = "990EVO", FullName = "Samsung SSD 990 EVO Plus 4TB", Capacity = "4T", SizeGb = 3726, Temperature = 32, IsSsd = true },
                    new StorageReading { Name = "Ti7100", FullName = "ZHITAI TiPlus7100 1TB", Capacity = "1T", SizeGb = 954, Temperature = 40, IsSsd = true },
                    new StorageReading { Name = "WD40EZRZ", FullName = "WDC WD40EZRZ-00GXCB0 4TB", Capacity = "4T", SizeGb = 3726, Temperature = 32, IsSsd = false },
                },
            };
            Publish(snapshot);
            if (_cts.Token.WaitHandle.WaitOne(1000))
                return;
        }
    }

    private void PollLoop()
    {
        Computer? computer = null;
        int failures = 0;
        int tick = 0;

        while (!_cts.IsCancellationRequested)
        {
            if (_paused)
            {
                if (_cts.Token.WaitHandle.WaitOne(500))
                    return;
                continue;
            }

            try
            {
                if (computer == null)
                {
                    computer = new Computer
                    {
                        IsCpuEnabled = true,
                        IsGpuEnabled = true,
                        IsMotherboardEnabled = true,
                        IsControllerEnabled = false,
                        IsMemoryEnabled = false,
                        IsStorageEnabled = false,
                        IsNetworkEnabled = false,
                        IsPsuEnabled = false,
                    };
                    computer.Open();
                }

                bool includeBoard = tick % 2 == 0;
                foreach (var hardware in computer.Hardware)
                {
                    bool isBoard = hardware.HardwareType == HardwareType.Motherboard ||
                                   hardware.HardwareType.ToString() == "SuperIO";
                    if (!isBoard || includeBoard)
                        UpdateHardwareTree(hardware);
                }

                tick++;
                Publish(Read(computer));
                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                try { computer?.Close(); } catch { }
                computer = null;
                Publish(new SensorSnapshot
                {
                    Error = ex.Message,
                    CpuPresent = true,
                    GpuPresent = true,
                });

                if (_cts.Token.WaitHandle.WaitOne(failures >= 3 ? 15000 : 3000))
                    break;
                continue;
            }

            if (_cts.Token.WaitHandle.WaitOne(1000))
                break;
        }

        try { computer?.Close(); } catch { }
    }

    private void Publish(SensorSnapshot snapshot)
    {
        _latest = snapshot;
        Updated?.Invoke(snapshot);
    }

    private SensorSnapshot Read(Computer computer)
    {
        string cpuName = "CPU";
        string gpuName = "GPU";
        bool cpuPresent = false, gpuPresent = false;
        double? cpuTemp = null, cpuLoad = null, cpuClock = null;
        double? gpuTemp = null, gpuLoad = null, gpuPower = null;
        double? gpuMemUsed = null, gpuMemTotal = null;

        var cpuClocks = new List<double>();
        var cpuCoreTemps = new List<double>();
        var boardFans = new List<FanReading>();
        var gpuFans = new List<FanReading>();
        int boardFanIndex = 0;
        int gpuFanIndex = 0;

        foreach (var hardware in computer.Hardware)
        {
            if (hardware.HardwareType == HardwareType.Cpu)
            {
                cpuPresent = true;
                cpuName = ShortCpuName(hardware.Name);

                cpuTemp = Pick(hardware.Sensors, SensorType.Temperature, "CPU Package")?.Value
                          ?? Pick(hardware.Sensors, SensorType.Temperature, "Core Average")?.Value
                          ?? Pick(hardware.Sensors, SensorType.Temperature, "Core Max")?.Value;

                if (cpuTemp == null)
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature &&
                            sensor.Value is float value &&
                            sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                        {
                            cpuCoreTemps.Add(value);
                        }
                    }
                    if (cpuCoreTemps.Count > 0)
                        cpuTemp = cpuCoreTemps.Max();
                }

                cpuLoad = Pick(hardware.Sensors, SensorType.Load, "CPU Total")?.Value;

                foreach (var sensor in hardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Clock &&
                        sensor.Value is float clock &&
                        sensor.Name.Contains("CPU Core", StringComparison.OrdinalIgnoreCase) &&
                        !sensor.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                    {
                        cpuClocks.Add(clock);
                    }
                }
                if (cpuClocks.Count > 0)
                    cpuClock = cpuClocks.Average();
            }
            else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            {
                gpuPresent = true;
                gpuName = ShortGpuName(hardware.Name);

                gpuTemp = Pick(hardware.Sensors, SensorType.Temperature, "GPU Core")?.Value
                          ?? Pick(hardware.Sensors, SensorType.Temperature, "GPU Hot Spot")?.Value;
                gpuLoad = Pick(hardware.Sensors, SensorType.Load, "GPU Core")?.Value
                          ?? Pick(hardware.Sensors, SensorType.Load, "D3D 3D")?.Value;
                gpuPower = Pick(hardware.Sensors, SensorType.Power, "GPU Package")?.Value
                           ?? Pick(hardware.Sensors, SensorType.Power, "GPU Board Power")?.Value
                           ?? Pick(hardware.Sensors, SensorType.Power, "GPU Power")?.Value;
                gpuMemUsed = Pick(hardware.Sensors, SensorType.SmallData, "GPU Memory Used")?.Value;
                gpuMemTotal = Pick(hardware.Sensors, SensorType.SmallData, "GPU Memory Total")?.Value;
                CollectGpuFans(hardware, gpuFans, ref gpuFanIndex);
            }
            else if (hardware.HardwareType == HardwareType.Motherboard ||
                     hardware.HardwareType.ToString() == "SuperIO")
            {
                // SuperIO fan channels usually live under the motherboard as sub-hardware.
                CollectBoardFans(hardware, boardFans, ref boardFanIndex, _seenBoardFans);
            }
        }

        var fans = new List<FanReading>(boardFans.Count + gpuFans.Count);
        fans.AddRange(boardFans);
        fans.AddRange(gpuFans);

        var (disks, storageDenied, storageLoaded) = GetStorage();
        var (memoryLoad, memoryUsed, memoryTotal) = MemoryReader.Read();

        return new SensorSnapshot
        {
            CpuPresent = cpuPresent,
            GpuPresent = gpuPresent,
            CpuName = cpuName,
            GpuName = gpuName,
            CpuTemp = cpuTemp,
            CpuLoad = cpuLoad,
            CpuClockMhz = cpuClock,
            MemoryLoad = memoryLoad,
            MemoryUsedGb = memoryUsed,
            MemoryTotalGb = memoryTotal,
            GpuTemp = gpuTemp,
            GpuLoad = gpuLoad,
            GpuPower = gpuPower,
            GpuMemoryUsedMb = gpuMemUsed,
            GpuMemoryTotalMb = gpuMemTotal,
            Fans = fans,
            Disks = disks,
            StorageAccessDenied = storageDenied,
            StorageLoaded = storageLoaded,
        };
    }

    private (IReadOnlyList<StorageReading> Disks, bool Denied, bool Loaded) GetStorage()
    {
        if (!_storageEnabled)
            return (Array.Empty<StorageReading>(), false, false);

        lock (_storageLock)
        {
            long now = Environment.TickCount64;
            if (now - _storageFetchedAt < 5000)
                return (_storageCache, _storageAccessDenied, true);

            try
            {
                _storageCache = StorageReader.Read();
                _storageAccessDenied = false;
            }
            catch (Exception ex)
            {
                _storageCache = Array.Empty<StorageReading>();
                _storageAccessDenied = StorageReader.IsAccessDenied(ex);
            }

            _storageFetchedAt = now;
            return (_storageCache, _storageAccessDenied, true);
        }
    }

    private static void CollectBoardFans(IHardware hardware, List<FanReading> fans, ref int index,
        HashSet<string> seen)
    {
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType != SensorType.Fan || sensor.Value is not float rpm)
                continue;

            string key = $"{hardware.Name}|{sensor.Name}";
            bool wasSpinning = seen.Contains(key);
            if (rpm > 0.5f)
                seen.Add(key);
            else if (!wasSpinning)
                continue;

            index++;
            fans.Add(new FanReading
            {
                Label = BoardFanLabel(sensor.Name, index),
                SensorName = sensor.Name,
                HardwareName = hardware.Name,
                Rpm = Math.Round(rpm, 0),
            });
        }

        foreach (var subHardware in hardware.SubHardware)
            CollectBoardFans(subHardware, fans, ref index, seen);
    }

    private static void CollectGpuFans(IHardware hardware, List<FanReading> fans, ref int gpuIndex)
    {
        var sensors = new List<ISensor>();
        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Fan && sensor.Value is float)
                sensors.Add(sensor);
        }

        if (sensors.Count == 0)
            return;

        gpuIndex++;
        string label = gpuIndex == 1 ? "显卡" : $"显卡{gpuIndex}";
        double rpm = sensors.Max(s => (double)(s.Value ?? 0));

        // A graphics card normally exposes one tach per physical fan, but both
        // fans are driven by the same curve. Show one row per GPU and keep the
        // individual channel readings in the tooltip.
        string details = string.Join(" / ", sensors.Select(s => $"{s.Name} {s.Value:0}"));

        fans.Add(new FanReading
        {
            Label = label,
            SensorName = details,
            HardwareName = hardware.Name,
            Rpm = Math.Round(rpm, 0),
            IsGpu = true,
        });
    }

    private static string BoardFanLabel(string sensorName, int fallbackIndex)
    {
        if (sensorName.Contains("Pump", StringComparison.OrdinalIgnoreCase))
            return "水泵";
        if (sensorName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            return "CPU风扇";
        if (sensorName.Contains("Chassis", StringComparison.OrdinalIgnoreCase))
            return $"机箱{TrailingNumber(sensorName) ?? fallbackIndex}";
        return $"主板{TrailingNumber(sensorName) ?? fallbackIndex}";
    }

    private static int? TrailingNumber(string name)
    {
        var match = Regex.Match(name, @"(\d+)\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out int number) ? number : null;
    }

    private static ISensor? Pick(IReadOnlyList<ISensor> sensors, SensorType type, string name)
    {
        ISensor? exact = null;
        ISensor? partial = null;
        foreach (var sensor in sensors)
        {
            if (sensor.SensorType != type || sensor.Value == null)
                continue;
            if (sensor.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                exact = sensor;
                break;
            }
            if (partial == null && sensor.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                partial = sensor;
        }
        return exact ?? partial;
    }

    private static string ShortCpuName(string name)
    {
        string result = name.Replace("(R)", string.Empty).Replace("(TM)", string.Empty);
        result = Regex.Replace(result, @"^\d+(?:st|nd|rd|th)\s+Gen\s+Intel\s+Core\s+", string.Empty,
            RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"^Intel\s+Core\s+", string.Empty, RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"\s+", " ").Trim();
        return result.Length == 0 ? name : result;
    }

    private static string ShortGpuName(string name)
    {
        string result = name
            .Replace("NVIDIA GeForce ", string.Empty)
            .Replace("NVIDIA ", string.Empty)
            .Replace("AMD Radeon ", string.Empty)
            .Replace("AMD ", string.Empty)
            .Replace("Intel(R) ", string.Empty)
            .Replace("(R)", string.Empty)
            .Replace("(TM)", string.Empty)
            .Trim();
        return result.Length == 0 ? name : result;
    }

    private static void UpdateHardwareTree(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            UpdateHardwareTree(subHardware);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _thread?.Join(2000); } catch { }
        _cts.Dispose();
    }
}
