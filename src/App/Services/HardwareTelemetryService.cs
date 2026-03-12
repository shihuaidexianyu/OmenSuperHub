using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using LibreComputer = LibreHardwareMonitor.Hardware.Computer;
using LibreIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreISensor = LibreHardwareMonitor.Hardware.ISensor;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;
using static OmenSuperHub.OmenHardware;

namespace OmenSuperHub {
  internal sealed class HardwareTelemetryRequest {
    public float CurrentCpuTemperature { get; set; }
    public float CurrentGpuTemperature { get; set; }
    public float CurrentCpuPowerWatts { get; set; }
    public float CurrentGpuPowerWatts { get; set; }
    public float RespondSpeed { get; set; }
    public bool MonitorGpu { get; set; }
    public bool OpenLibEnabled { get; set; }
  }

  internal sealed class HardwareTelemetrySnapshot {
    public bool OpenLibEnabled { get; set; }
    public float CpuTemperature { get; set; }
    public float GpuTemperature { get; set; }
    public float CpuPowerWatts { get; set; }
    public float GpuPowerWatts { get; set; }
    public OmenGfxMode GraphicsMode { get; set; }
    public OmenGpuStatus GpuStatus { get; set; }
    public OmenSystemDesignData SystemDesignData { get; set; }
    public OmenSmartAdapterStatus SmartAdapterStatus { get; set; }
    public OmenFanTypeInfo FanTypeInfo { get; set; }
    public OmenKeyboardType KeyboardType { get; set; }
    public BatteryTelemetry BatteryTelemetry { get; set; }
    public List<TemperatureSensorReading> TemperatureSensors { get; set; }
  }

  internal sealed class HardwareTelemetryService {
    readonly LibreComputer libreComputer;
    readonly object stateLock = new object();
    int advancedStatusTick;
    int temperatureSensorRefreshTick;
    int advancedStatusRefreshInProgress;
    OmenGfxMode currentGfxMode = OmenGfxMode.Unknown;
    OmenGpuStatus currentGpuStatus;
    OmenSystemDesignData currentSystemDesignData;
    OmenSmartAdapterStatus currentSmartAdapterStatus = OmenSmartAdapterStatus.Unknown;
    OmenFanTypeInfo currentFanTypeInfo;
    OmenKeyboardType currentKeyboardType = OmenKeyboardType.Unknown;
    BatteryTelemetry currentBatteryTelemetry;
    List<TemperatureSensorReading> currentTemperatureSensors = new List<TemperatureSensorReading>();

    static readonly string[] CpuPreferredSensorKeywords = {
      "cpu package",
      "package",
      "core max",
      "cpu core",
      "p-core",
      "e-core"
    };

    static readonly string[] GpuPreferredSensorKeywords = {
      "gpu hot spot",
      "gpu hotspot",
      "hot spot",
      "hotspot",
      "junction",
      "gpu core",
      "gpu temperature",
      "memory junction",
      "memory"
    };

    public HardwareTelemetryService(LibreComputer libreComputer) {
      this.libreComputer = libreComputer;
    }

    public HardwareTelemetrySnapshot Poll(HardwareTelemetryRequest request) {
      float openTempCPU = -300f;
      float libreTempCPU = -300f;
      float tempCPU = 50f;
      float openPowerCPU = -1f;
      float librePowerCPU = -1f;
      int bestGpuTempScore = int.MinValue;
      int bestGpuPowerScore = int.MinValue;
      float bestGpuTemp = float.NaN;
      float bestGpuPower = float.NaN;
      bool gpuTempFound;
      bool gpuPowerFound;
      float nextGpuTemp = request.CurrentGpuTemperature;
      float nextGpuPower = request.CurrentGpuPowerWatts;
      bool openLibEnabled = request.OpenLibEnabled;

      foreach (LibreIHardware hardware in libreComputer.Hardware) {
        bool isCpu = hardware.HardwareType == LibreHardwareType.Cpu;
        bool isGpu = IsGpuHardware(hardware.HardwareType);
        if (!isCpu && !isGpu) {
          continue;
        }

        hardware.Update();
        foreach (LibreISensor sensor in hardware.Sensors) {
          if (isCpu) {
            if (sensor.Name == "CPU Package" && sensor.SensorType == LibreSensorType.Temperature) {
              libreTempCPU = (int)sensor.Value.GetValueOrDefault();
            }
            if (sensor.Name == "CPU Package" && sensor.SensorType == LibreSensorType.Power) {
              librePowerCPU = sensor.Value.GetValueOrDefault();
            }
          } else if (request.MonitorGpu && isGpu) {
            UpdateGpuSensorCandidate(hardware, sensor, ref bestGpuTempScore, ref bestGpuTemp, ref bestGpuPowerScore, ref bestGpuPower);
          }
        }

        if (request.MonitorGpu && isGpu && hardware.SubHardware != null) {
          foreach (LibreIHardware subHardware in hardware.SubHardware) {
            if (subHardware == null) {
              continue;
            }

            subHardware.Update();
            foreach (LibreISensor subSensor in subHardware.Sensors) {
              UpdateGpuSensorCandidate(hardware, subSensor, ref bestGpuTempScore, ref bestGpuTemp, ref bestGpuPowerScore, ref bestGpuPower);
            }
          }
        }
      }

      gpuTempFound = bestGpuTempScore > int.MinValue && !float.IsNaN(bestGpuTemp);
      gpuPowerFound = bestGpuPowerScore > int.MinValue && !float.IsNaN(bestGpuPower);
      if (request.MonitorGpu && gpuTempFound) {
        nextGpuTemp = bestGpuTemp * request.RespondSpeed + request.CurrentGpuTemperature * (1.0f - request.RespondSpeed);
      }
      if (request.MonitorGpu) {
        if (gpuPowerFound) {
          nextGpuPower = (int)(bestGpuPower * 10) == 5900 ? 0f : Math.Max(0f, bestGpuPower);
        } else {
          nextGpuPower = 0f;
        }
      }

      if (temperatureSensorRefreshTick <= 0) {
        RefreshTemperatureSensorsSnapshot();
        temperatureSensorRefreshTick = 2;
      } else {
        temperatureSensorRefreshTick--;
      }

      if (openLibEnabled && libreTempCPU > -299f && librePowerCPU >= 0f) {
        openLibEnabled = false;
      }

      if (openTempCPU < -299f) {
        if (libreTempCPU > -299f) {
          tempCPU = libreTempCPU;
        }
      } else {
        tempCPU = openTempCPU;
      }

      float nextCpuTemp = tempCPU * request.RespondSpeed + request.CurrentCpuTemperature * (1.0f - request.RespondSpeed);
      float nextCpuPower = openPowerCPU < 0f
        ? (librePowerCPU >= 0f ? librePowerCPU : request.CurrentCpuPowerWatts)
        : openPowerCPU;

      if (advancedStatusTick <= 0) {
        ScheduleAdvancedHardwareStatusRefresh();
        advancedStatusTick = 4;
      } else {
        advancedStatusTick--;
      }

      if (!libreComputer.IsGpuEnabled) {
        libreComputer.IsGpuEnabled = true;
      }

      lock (stateLock) {
        return new HardwareTelemetrySnapshot {
          OpenLibEnabled = openLibEnabled,
          CpuTemperature = nextCpuTemp,
          GpuTemperature = nextGpuTemp,
          CpuPowerWatts = nextCpuPower,
          GpuPowerWatts = nextGpuPower,
          GraphicsMode = currentGfxMode,
          GpuStatus = CloneGpuStatus(currentGpuStatus),
          SystemDesignData = CloneSystemDesignData(currentSystemDesignData),
          SmartAdapterStatus = currentSmartAdapterStatus,
          FanTypeInfo = CloneFanTypeInfo(currentFanTypeInfo),
          KeyboardType = currentKeyboardType,
          BatteryTelemetry = CloneBatteryTelemetry(currentBatteryTelemetry),
          TemperatureSensors = CloneTemperatureReadings(currentTemperatureSensors)
        };
      }
    }

    public float SelectControlTemperature(bool preferCpu, IList<TemperatureSensorReading> readings, float fallback, out string source) {
      float bestPreferred = float.MinValue;
      string bestPreferredName = null;
      float bestGeneral = float.MinValue;
      string bestGeneralName = null;

      if (readings != null) {
        foreach (var reading in readings) {
          if (reading == null || string.IsNullOrWhiteSpace(reading.Name))
            continue;
          if (float.IsNaN(reading.Celsius) || reading.Celsius < -50f || reading.Celsius > 200f)
            continue;

          string sensorName = reading.Name;
          bool domainMatch = preferCpu ? LooksLikeCpuSensor(sensorName) : LooksLikeGpuSensor(sensorName);
          if (!domainMatch)
            continue;

          if (reading.Celsius > bestGeneral) {
            bestGeneral = reading.Celsius;
            bestGeneralName = sensorName;
          }

          bool preferredMatch = preferCpu
            ? ContainsAnyKeyword(sensorName, CpuPreferredSensorKeywords)
            : ContainsAnyKeyword(sensorName, GpuPreferredSensorKeywords);
          if (!preferredMatch)
            continue;

          if (reading.Celsius > bestPreferred) {
            bestPreferred = reading.Celsius;
            bestPreferredName = sensorName;
          }
        }
      }

      if (bestPreferredName != null) {
        source = bestPreferredName;
        return bestPreferred;
      }

      if (bestGeneralName != null) {
        source = bestGeneralName;
        return bestGeneral;
      }

      source = "fallback";
      return fallback;
    }

    public static float? GetBatteryPowerWatts(BatteryTelemetry telemetry) {
      if (telemetry == null)
        return null;

      if (telemetry.Discharging && telemetry.DischargeRateMilliwatts > 0)
        return telemetry.DischargeRateMilliwatts / 1000f;

      if (telemetry.Charging && telemetry.ChargeRateMilliwatts > 0)
        return telemetry.ChargeRateMilliwatts / 1000f;

      return null;
    }

    static bool IsGpuHardware(LibreHardwareType type) {
      return type.ToString().StartsWith("Gpu", StringComparison.OrdinalIgnoreCase);
    }

    static int GetGpuHardwarePriority(LibreHardwareType type) {
      string name = type.ToString().ToLowerInvariant();
      if (name.Contains("nvidia")) return 300;
      if (name.Contains("amd")) return 200;
      if (name.Contains("intel")) return 100;
      if (name.StartsWith("gpu")) return 50;
      return 0;
    }

    static int ScoreGpuTempSensorName(string sensorName) {
      if (string.IsNullOrWhiteSpace(sensorName)) return 10;
      string name = sensorName.ToLowerInvariant();
      if (name.Contains("hot spot") || name.Contains("hotspot")) return 95;
      if (name.Contains("core")) return 90;
      if (name.Contains("junction")) return 85;
      if (name.Contains("memory")) return 70;
      return 50;
    }

    static int ScoreGpuPowerSensorName(string sensorName) {
      if (string.IsNullOrWhiteSpace(sensorName)) return 10;
      string name = sensorName.ToLowerInvariant();
      if (name.Contains("package")) return 95;
      if (name.Contains("total board")) return 92;
      if (name.Contains("board")) return 90;
      if (name.Contains("gpu power")) return 88;
      if (name.Contains("core")) return 80;
      return 50;
    }

    static void UpdateGpuSensorCandidate(LibreIHardware hardware, LibreISensor sensor, ref int bestTempScore, ref float bestTemp, ref int bestPowerScore, ref float bestPower) {
      if (sensor == null || !sensor.Value.HasValue) {
        return;
      }

      int hardwareScore = GetGpuHardwarePriority(hardware.HardwareType);
      if (sensor.SensorType == LibreSensorType.Temperature) {
        int score = hardwareScore + ScoreGpuTempSensorName(sensor.Name);
        if (score > bestTempScore) {
          bestTempScore = score;
          bestTemp = sensor.Value.GetValueOrDefault();
        }
      } else if (sensor.SensorType == LibreSensorType.Power) {
        int score = hardwareScore + ScoreGpuPowerSensorName(sensor.Name);
        if (score > bestPowerScore) {
          bestPowerScore = score;
          bestPower = sensor.Value.GetValueOrDefault();
        }
      }
    }

    void RefreshTemperatureSensorsSnapshot() {
      var readings = new List<TemperatureSensorReading>();
      foreach (LibreIHardware hardware in libreComputer.Hardware) {
        CollectTemperatureSensorsRecursive(hardware, hardware?.Name, readings);
      }

      readings.Sort((a, b) => b.Celsius.CompareTo(a.Celsius));
      if (readings.Count > 96) {
        readings = readings.GetRange(0, 96);
      }

      lock (stateLock) {
        currentTemperatureSensors = readings;
      }
    }

    static void CollectTemperatureSensorsRecursive(LibreIHardware hardware, string path, List<TemperatureSensorReading> readings) {
      if (hardware == null || readings == null) {
        return;
      }

      hardware.Update();
      foreach (LibreISensor sensor in hardware.Sensors) {
        if (sensor == null || sensor.SensorType != LibreSensorType.Temperature || !sensor.Value.HasValue) {
          continue;
        }

        float value = sensor.Value.GetValueOrDefault();
        if (float.IsNaN(value) || value < -50f || value > 200f) {
          continue;
        }

        string hardwareName = string.IsNullOrWhiteSpace(path) ? (hardware.Name ?? "Unknown") : path;
        string sensorName = string.IsNullOrWhiteSpace(sensor.Name) ? "Temperature" : sensor.Name;
        readings.Add(new TemperatureSensorReading {
          Name = $"{hardwareName} / {sensorName}",
          Celsius = value
        });
      }

      if (hardware.SubHardware == null) {
        return;
      }

      foreach (LibreIHardware subHardware in hardware.SubHardware) {
        if (subHardware == null) {
          continue;
        }

        string subPath = string.IsNullOrWhiteSpace(path)
          ? subHardware.Name
          : $"{path} > {subHardware.Name}";
        CollectTemperatureSensorsRecursive(subHardware, subPath, readings);
      }
    }

    static bool LooksLikeCpuSensor(string name) {
      if (string.IsNullOrWhiteSpace(name))
        return false;

      string lower = name.ToLowerInvariant();
      if (lower.Contains("gpu") || lower.Contains("nvidia") || lower.Contains("radeon"))
        return false;

      return lower.Contains("cpu") ||
             lower.Contains("p-core") ||
             lower.Contains("e-core") ||
             lower.Contains("ia core") ||
             lower.Contains("package") ||
             lower.Contains("core");
    }

    static bool LooksLikeGpuSensor(string name) {
      if (string.IsNullOrWhiteSpace(name))
        return false;

      string lower = name.ToLowerInvariant();
      return lower.Contains("gpu") ||
             lower.Contains("nvidia") ||
             lower.Contains("radeon") ||
             lower.Contains("graphics") ||
             lower.Contains("hot spot") ||
             lower.Contains("hotspot") ||
             lower.Contains("junction");
    }

    static bool ContainsAnyKeyword(string name, string[] keywords) {
      if (string.IsNullOrWhiteSpace(name) || keywords == null)
        return false;

      string lower = name.ToLowerInvariant();
      foreach (string keyword in keywords) {
        if (!string.IsNullOrWhiteSpace(keyword) && lower.Contains(keyword))
          return true;
      }

      return false;
    }

    void ScheduleAdvancedHardwareStatusRefresh() {
      if (Interlocked.Exchange(ref advancedStatusRefreshInProgress, 1) != 0)
        return;

      Task.Run(() => {
        try {
          RefreshAdvancedHardwareStatus();
        } finally {
          Interlocked.Exchange(ref advancedStatusRefreshInProgress, 0);
        }
      });
    }

    void RefreshAdvancedHardwareStatus() {
      OmenGfxMode nextGfxMode;
      OmenGpuStatus nextGpuStatus;
      OmenSystemDesignData nextSystemDesignData;
      OmenSmartAdapterStatus nextSmartAdapterStatus;
      OmenFanTypeInfo nextFanTypeInfo;
      OmenKeyboardType nextKeyboardType;
      BatteryTelemetry nextBatteryTelemetry;

      lock (stateLock) {
        nextGfxMode = currentGfxMode;
        nextGpuStatus = currentGpuStatus;
        nextSystemDesignData = currentSystemDesignData;
        nextSmartAdapterStatus = currentSmartAdapterStatus;
        nextFanTypeInfo = currentFanTypeInfo;
        nextKeyboardType = currentKeyboardType;
        nextBatteryTelemetry = currentBatteryTelemetry;
      }

      try {
        nextGfxMode = GetGraphicsMode();
      } catch {
      }

      try {
        var gpuStatus = GetGpuStatus();
        if (gpuStatus != null)
          nextGpuStatus = gpuStatus;
      } catch {
      }

      try {
        var designData = GetSystemDesignData();
        if (designData != null)
          nextSystemDesignData = designData;
      } catch {
      }

      try {
        nextSmartAdapterStatus = GetSmartAdapterStatus();
      } catch {
      }

      try {
        var fanTypeInfo = GetFanTypeInfo();
        if (fanTypeInfo != null)
          nextFanTypeInfo = fanTypeInfo;
      } catch {
      }

      try {
        nextKeyboardType = GetKeyboardType();
      } catch {
      }

      try {
        nextBatteryTelemetry = ReadBatteryTelemetry();
      } catch {
        nextBatteryTelemetry = null;
      }

      lock (stateLock) {
        currentGfxMode = nextGfxMode;
        currentGpuStatus = nextGpuStatus;
        currentSystemDesignData = nextSystemDesignData;
        currentSmartAdapterStatus = nextSmartAdapterStatus;
        currentFanTypeInfo = nextFanTypeInfo;
        currentKeyboardType = nextKeyboardType;
        currentBatteryTelemetry = nextBatteryTelemetry;
      }
    }

    static BatteryTelemetry ReadBatteryTelemetry() {
      using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT PowerOnline, Charging, Discharging, DischargeRate, ChargeRate, RemainingCapacity, Voltage FROM BatteryStatus")) {
        foreach (ManagementObject battery in searcher.Get()) {
          return new BatteryTelemetry {
            PowerOnline = Convert.ToBoolean(battery["PowerOnline"] ?? false),
            Charging = Convert.ToBoolean(battery["Charging"] ?? false),
            Discharging = Convert.ToBoolean(battery["Discharging"] ?? false),
            DischargeRateMilliwatts = Convert.ToInt32(battery["DischargeRate"] ?? 0),
            ChargeRateMilliwatts = Convert.ToInt32(battery["ChargeRate"] ?? 0),
            RemainingCapacityMilliwattHours = Convert.ToInt32(battery["RemainingCapacity"] ?? 0),
            VoltageMillivolts = Convert.ToInt32(battery["Voltage"] ?? 0)
          };
        }
      }

      return null;
    }

    static OmenGpuStatus CloneGpuStatus(OmenGpuStatus source) {
      if (source == null) return null;
      return new OmenGpuStatus {
        CustomTgpEnabled = source.CustomTgpEnabled,
        PpabEnabled = source.PpabEnabled,
        DState = source.DState,
        ThermalThreshold = source.ThermalThreshold,
        RawData = source.RawData == null ? null : (byte[])source.RawData.Clone()
      };
    }

    static OmenSystemDesignData CloneSystemDesignData(OmenSystemDesignData source) {
      if (source == null) return null;
      return new OmenSystemDesignData {
        PowerFlags = source.PowerFlags,
        ThermalPolicyVersion = source.ThermalPolicyVersion,
        FeatureFlags = source.FeatureFlags,
        DefaultPl4 = source.DefaultPl4,
        BiosOverclockingSupport = source.BiosOverclockingSupport,
        MiscFlags = source.MiscFlags,
        DefaultConcurrentTdp = source.DefaultConcurrentTdp,
        SoftwareFanControlSupported = source.SoftwareFanControlSupported,
        ExtremeModeSupported = source.ExtremeModeSupported,
        ExtremeModeUnlocked = source.ExtremeModeUnlocked,
        GraphicsSwitcherSupported = source.GraphicsSwitcherSupported,
        RawData = source.RawData == null ? null : (byte[])source.RawData.Clone()
      };
    }

    static OmenFanTypeInfo CloneFanTypeInfo(OmenFanTypeInfo source) {
      if (source == null) return null;
      return new OmenFanTypeInfo {
        RawValue = source.RawValue,
        Fan1Type = source.Fan1Type,
        Fan2Type = source.Fan2Type
      };
    }

    static BatteryTelemetry CloneBatteryTelemetry(BatteryTelemetry source) {
      if (source == null) return null;
      return new BatteryTelemetry {
        PowerOnline = source.PowerOnline,
        Charging = source.Charging,
        Discharging = source.Discharging,
        DischargeRateMilliwatts = source.DischargeRateMilliwatts,
        ChargeRateMilliwatts = source.ChargeRateMilliwatts,
        RemainingCapacityMilliwattHours = source.RemainingCapacityMilliwattHours,
        VoltageMillivolts = source.VoltageMillivolts
      };
    }

    static List<TemperatureSensorReading> CloneTemperatureReadings(IList<TemperatureSensorReading> readings) {
      var snapshot = new List<TemperatureSensorReading>();
      if (readings == null) {
        return snapshot;
      }

      foreach (var reading in readings) {
        if (reading == null) {
          continue;
        }

        snapshot.Add(new TemperatureSensorReading {
          Name = reading.Name,
          Celsius = reading.Celsius
        });
      }
      return snapshot;
    }
  }
}
