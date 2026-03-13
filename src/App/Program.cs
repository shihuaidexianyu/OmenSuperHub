using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.TaskScheduler;
using System.Reflection;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Management;
using TaskEx = System.Threading.Tasks.Task;
using LibreComputer = LibreHardwareMonitor.Hardware.Computer;
using LibreIHardware = LibreHardwareMonitor.Hardware.IHardware;
using LibreHardwareType = LibreHardwareMonitor.Hardware.HardwareType;
using LibreISensor = LibreHardwareMonitor.Hardware.ISensor;
using LibreSensorType = LibreHardwareMonitor.Hardware.SensorType;
using static OmenSuperHub.OmenHardware;
using System.IO.Pipes;

namespace OmenSuperHub {
  static class Program {
    internal static void ApplyFanModeSetting(string mode) {
      if (mode == "performance") {
        fanMode = "performance";
        SetFanMode(0x31);
      } else {
        fanMode = "default";
        SetFanMode(0x30);
      }

      RestoreCPUPower();
      SaveConfig("FanMode");
    }

    internal static void ApplyFanControlSetting(string controlValue) {
      if (controlValue == "auto") {
        fanControl = "auto";
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
      } else if (controlValue == "max") {
        fanControl = "max";
        SetMaxFanSpeedOn();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
      } else if (controlValue.EndsWith(" RPM")) {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        ApplyManualFanRpm(controlValue);
      }

      SaveConfig("FanControl");
    }

    internal static void ApplyFanTableSetting(string value) {
      fanTable = value == "cool" ? "cool" : "silent";
      LoadFanConfig(fanTable == "cool" ? "cool.txt" : "silent.txt");
      SaveConfig("FanTable");
    }

    internal static void ApplyTempSensitivitySetting(string value) {
      tempSensitivity = value;
      switch (value) {
        case "realtime":
          respondSpeed = 1f;
          break;
        case "medium":
          respondSpeed = 0.1f;
          break;
        case "low":
          respondSpeed = 0.04f;
          break;
        default:
          tempSensitivity = "high";
          respondSpeed = 0.4f;
          break;
      }

      SaveConfig("TempSensitivity");
    }

    internal static void ApplyCpuPowerSetting(string value) {
      if (value == "max") {
        cpuPower = "max";
        SetCpuPowerLimit(254);
      } else if (value.EndsWith(" W")) {
        int watt = int.Parse(value.Replace(" W", "").Trim());
        cpuPower = $"{watt} W";
        SetCpuPowerLimit((byte)watt);
      }

      powerController.Reset();
      SaveConfig("CpuPower");
    }

    internal static void ApplyGpuPowerSetting(string value) {
      gpuPower = value;
      switch (value) {
        case "max":
          SetMaxGpuPower();
          break;
        case "med":
          SetMedGpuPower();
          break;
        default:
          gpuPower = "min";
          SetMinGpuPower();
          break;
      }

      powerController.Reset();
      SaveConfig("GpuPower");
    }

    internal static void ApplyGpuClockSetting(int value) {
      gpuClock = value;
      SetGPUClockLimit(gpuClock);
      SaveConfig("GpuClock");
    }

    internal static void ApplyFloatingBarSetting(bool enabled) {
      floatingBar = enabled ? "on" : "off";
      RefreshShellStatus();
      SaveConfig("FloatingBar");
    }

    internal static void ApplyFloatingBarLocationSetting(string location) {
      floatingBarLoc = location == "right" ? "right" : "left";
      RefreshShellStatus();
      SaveConfig("FloatingBarLoc");
    }

    internal static void ApplySmartPowerControlSetting(bool enabled) {
      smartPowerControlEnabled = enabled;
      powerController.Reset();

      if (!enabled) {
        RestoreCPUPower();
        RestoreGpuPower();
        if (fanControl == "auto")
          SetMaxFanSpeedOff();
        smartPowerControlState = "manual";
        smartPowerControlReason = "disabled";
      }

      SaveConfig("SmartPowerControl");
    }

    internal static PowerControlTuning GetPowerControlTuningSnapshot() {
      lock (powerControlLock) {
        return powerController.GetTuningSnapshot();
      }
    }

    internal static PowerControlTuning GetDefaultPowerControlTuning() {
      return PowerController.CreateDefaultTuning();
    }

    internal static void ApplyPowerControlTuning(PowerControlTuning tuning) {
      if (tuning == null) {
        return;
      }

      lock (powerControlLock) {
        powerController.UpdateTuning(tuning);
      }
      SavePowerControlTuning();
    }

    internal static void ResetPowerControlTuningToDefault() {
      ApplyPowerControlTuning(GetDefaultPowerControlTuning());
    }

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static float CPUTemp = 50;
    static float GPUTemp = 40;
    static float CPUPower = 0;
    static float GPUPower = 0;
    static OmenGfxMode currentGfxMode = OmenGfxMode.Unknown;
    static OmenGpuStatus currentGpuStatus;
    static OmenSystemDesignData currentSystemDesignData;
    static OmenSmartAdapterStatus currentSmartAdapterStatus = OmenSmartAdapterStatus.Unknown;
    static OmenFanTypeInfo currentFanTypeInfo;
    static OmenKeyboardType currentKeyboardType = OmenKeyboardType.Unknown;
    static BatteryTelemetry currentBatteryTelemetry;
    static int DBVersion = 2, countDB = 0, countDBInit = 5, tryTimes = 0, CPULimitDB = 25;
    static int textSize = 48;
    static int countRestore = 0, gpuClock = 0;
    static int alreadyRead = 0, alreadyReadCode = 1000;
    const int FanMinRpm = 0;
    const int FanMaxRpm = 6400;
    const int FanRawStep = 100;
    const int FanMaxRawLevel = FanMaxRpm / FanRawStep;
    static string fanTable = "silent", fanMode = "performance", fanControl = "auto", tempSensitivity = "high", cpuPower = "max", gpuPower = "max", autoStart = "off", customIcon = "original", floatingBar = "off", floatingBarLoc = "left", omenKey = "default";
    static bool smartPowerControlEnabled = true;
    static string smartPowerControlState = "balanced";
    static string smartPowerControlReason = "stable";
    static float controlCpuTemperatureC = 0f;
    static float controlGpuTemperatureC = 0f;
    static string controlCpuSensorName = "fallback";
    static string controlGpuSensorName = "fallback";
    static float controlCpuTempWallC = 88f;
    static float controlGpuTempWallC = 79f;
    static float controlThermalFeedback = 0f;
    static float estimatedSystemPowerWatts = 0;
    static float targetSystemPowerWatts = 0;
    static int smartCpuLimitWatts = 0;
    static string smartGpuTier = "max";
    static bool smartFanBoostActive = false;
    static readonly object powerControlLock = new object();
    static readonly object temperatureSensorsLock = new object();
    static readonly PowerController powerController = new PowerController();
    static readonly ProcessCommandService processCommandService = new ProcessCommandService();
    static readonly AppSettingsService settingsService = new AppSettingsService();
    static readonly FanCurveService fanCurveService = new FanCurveService();
    static LibreComputer libreComputer = new LibreComputer() { IsCpuEnabled = true, IsGpuEnabled = true };
    static readonly HardwareTelemetryService hardwareTelemetryService = new HardwareTelemetryService(libreComputer);
    static readonly AppShellService shellService = new AppShellService();
    static bool monitorGPU = true, monitorFan = true, powerOnline = true;
    static List<int> fanSpeedNow = new List<int> { 20, 23 };
    static List<TemperatureSensorReading> currentTemperatureSensors = new List<TemperatureSensorReading>();
    static float respondSpeed = 0.4f;
    static System.Threading.Timer fanControlTimer;
    static System.Threading.Timer hardwarePollingTimer;
    static System.Windows.Forms.Timer checkFloatingTimer, optimiseTimer;
    static NamedPipeServerStream omenKeyPipeServer;
    static TaskEx omenKeyListenerTask;
    static int shutdownStarted = 0;
    static volatile bool checkFloating = false;
    static volatile bool isShuttingDown = false;

    static int ClampFanRpm(int rpm) {
      return Math.Max(FanMinRpm, Math.Min(FanMaxRpm, rpm));
    }

    static int FanRpmToRawLevel(int rpm) {
      int clampedRpm = ClampFanRpm(rpm);
      return Math.Max(0, Math.Min(FanMaxRawLevel, clampedRpm / FanRawStep));
    }

    static bool TryParseFanRpm(string value, out int rpm) {
      rpm = FanMinRpm;
      if (string.IsNullOrWhiteSpace(value)) {
        return false;
      }

      string normalized = value.Replace(" RPM", string.Empty).Trim();
      if (!int.TryParse(normalized, out int parsed)) {
        return false;
      }

      rpm = ClampFanRpm(parsed);
      return true;
    }

    static void ApplyManualFanRpm(string value) {
      if (!TryParseFanRpm(value, out int rpm)) {
        return;
      }

      fanControl = $"{rpm} RPM";
      int rawLevel = FanRpmToRawLevel(rpm);
      SetFanLevel(rawLevel, rawLevel);
    }

    [STAThread]
    static void Main(string[] args) {
      bool isNewInstance;
      using (Mutex mutex = new Mutex(true, "MyUniqueAppMutex", out isNewInstance)) {
        if (!isNewInstance) {
          return;
        }

        if (Environment.OSVersion.Version.Major >= 6) {
          SetProcessDPIAware();
        }

        AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);
        Application.ThreadException += new ThreadExceptionEventHandler(Application_ThreadException);
        Application.ApplicationExit += new EventHandler(OnApplicationExit);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        powerOnline = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online;
        Version version = Assembly.GetExecutingAssembly().GetName().Version;
        string versionString = version.ToString().Replace(".", "");
        alreadyReadCode = new Random(int.Parse(versionString)).Next(1000, 10000);

        // Initialize tray icon
        InitTrayIcon();

        // Initialize HardwareMonitorLib
        libreComputer.Open();

        optimiseTimer = new System.Windows.Forms.Timer();
        optimiseTimer.Interval = 30000;
        optimiseTimer.Tick += (s, e) => optimiseSchedule();
        optimiseTimer.Start();

        hardwarePollingTimer = new System.Threading.Timer((e) => {
          if (isShuttingDown) {
            return;
          }

          try {
            QueryHarware();
            if (monitorFan)
              fanSpeedNow = GetFanLevel();
            ApplySmartPowerControl();
          } catch {
          }
        }, null, 100, 1000);

        // Main loop to query CPU and GPU temperature every second
        fanControlTimer = new System.Threading.Timer((e) => {
          if (isShuttingDown) {
            return;
          }

          int fanSpeed1 = FanRpmToRawLevel(fanCurveService.GetFanSpeedForTemperature(CPUTemp, GPUTemp, monitorGPU, 0));
          int fanSpeed2 = FanRpmToRawLevel(fanCurveService.GetFanSpeedForTemperature(CPUTemp, GPUTemp, monitorGPU, 1));
          if (monitorFan) {
            if (fanSpeed1 != fanSpeedNow[0] || fanSpeed2 != fanSpeedNow[1]) {
              SetFanLevel(fanSpeed1, fanSpeed2);
            }
          } else
            SetFanLevel(fanSpeed1, fanSpeed2);
        }, null, 100, 1000);

        getOmenKeyTask();
        checkFloatingTimer = new System.Windows.Forms.Timer();
        checkFloatingTimer.Interval = 100;
        checkFloatingTimer.Tick += (s, e) => HandleFloatingBarToggle();
        checkFloatingTimer.Start();

        // Restore last setting
        RestoreConfig();

        if (alreadyRead != alreadyReadCode) {
          MainForm.Instance.ShowHelpSection();
          alreadyRead = alreadyReadCode;
          SaveConfig("AlreadyRead");
        }

        SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerChange);

        Application.Run();
      }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct DISPLAY_DEVICE {
      [MarshalAs(UnmanagedType.U4)]
      public int cb;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
      public string DeviceName;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceString;
      [MarshalAs(UnmanagedType.U4)]
      public DisplayDeviceStateFlags StateFlags;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceID;
      [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
      public string DeviceKey;
    }

    [Flags()]
    enum DisplayDeviceStateFlags : int {
      /// <summary>The device is part of the desktop.</summary>
      AttachedToDesktop = 0x1,
      MultiDriver = 0x2,
      /// <summary>The device is part of the desktop.</summary>
      PrimaryDevice = 0x4,
      /// <summary>Represents a pseudo device used to mirror application drawing for remoting or other purposes.</summary>
      MirroringDriver = 0x8,
      /// <summary>The device is VGA compatible.</summary>
      VGACompatible = 0x10,
      /// <summary>The device is removable; it cannot be the primary display.</summary>
      Removable = 0x20,
      /// <summary>The device has more display devices.</summary>
      ModesPruned = 0x8000000,
      Remote = 0x4000000,
      Disconnect = 0x2000000
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern bool EnumDisplayDevices(
        string lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags);

    static int flagStart = 0;
    static void optimiseSchedule() {
      // 延时等待风扇恢复响应
      if (flagStart < 5) {
        flagStart++;
        if (fanControl.Contains("max")) {
          SetMaxFanSpeedOn();
        } else if (fanControl.Contains(" RPM")) {
          SetMaxFanSpeedOff();
          ApplyManualFanRpm(fanControl);
        }
      }

      //定时通信避免功耗锁定
      GetFanCount();
    }

    static void OnPowerChange(object s, PowerModeChangedEventArgs e) {
      // 休眠重新启动
      if (e.Mode == PowerModes.Resume) {
        // GetFanCount
        SendOmenBiosWmi(0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);

        countRestore = 3;
      }

      // 检查电源模式是否发生变化
      if (e.Mode == PowerModes.StatusChange) {
        // 获取当前电源连接状态
        var powerStatus = SystemInformation.PowerStatus;
        if (powerStatus.PowerLineStatus == PowerLineStatus.Online) {
          Console.WriteLine("笔记本已连接到电源。");
          powerOnline = true;
        } else {
          Console.WriteLine("笔记本未连接到电源。");
          powerOnline = false;
        }
      }
    }

    // 任务计划程序
    static void AutoStartEnable() {
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;

      using (TaskService ts = new TaskService()) {
        TaskDefinition td = ts.NewTask();
        td.RegistrationInfo.Description = "Start OmenSuperHub with admin rights";
        td.Principal.RunLevel = TaskRunLevel.Highest;
        td.Actions.Add(new ExecAction(Path.Combine(currentPath, "OmenSuperHub.exe"), null, null));

        // 设置触发器：在用户登录时触发
        LogonTrigger logonTrigger = new LogonTrigger();
        //logonTrigger.Delay = TimeSpan.FromSeconds(10); // 延迟 10 秒
        td.Triggers.Add(logonTrigger);

        td.Settings.DisallowStartIfOnBatteries = false;
        td.Settings.StopIfGoingOnBatteries = false;
        td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        td.Settings.AllowHardTerminate = false;

        ts.RootFolder.RegisterTaskDefinition(@"OmenSuperHub", td);
        Console.WriteLine("任务已创建。");
      }

      CleanUpAndRemoveTasks();
    }

    static void AutoStartDisable() {
      using (TaskService ts = new TaskService()) {
        // 检查任务是否存在
        Microsoft.Win32.TaskScheduler.Task existingTask = ts.FindTask("OmenSuperHub");

        if (existingTask != null) {
          // 删除任务
          ts.RootFolder.DeleteTask("OmenSuperHub");
          Console.WriteLine("任务已删除。");
        } else {
          Console.WriteLine("任务不存在，无需删除。");
        }
      }
    }

    // 清理旧版自启
    public static void CleanUpAndRemoveTasks() {
      // 目标文件夹和文件定义
      string targetFolder = @"C:\Program Files\OmenSuperHub";
      string taskName = "Omen Boot";
      string file1 = @"C:\Windows\SysWOW64\silent.txt";
      string file2 = @"C:\Windows\SysWOW64\cool.txt";

      // 删除目标文件夹及其内容
      if (Directory.Exists(targetFolder)) {
        string command = $"rd /s /q \"{targetFolder}\"";
        var result = ExecuteCommand(command);
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine("旧文件夹不存在");
      }

      // 删除 file1
      if (File.Exists(file1)) {
        string command = $"del /f /q \"{file1}\"";
        var result = ExecuteCommand(command);
        Console.WriteLine($"文件已删除: {file1}");
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine($"文件不存在: {file1}");
      }

      // 删除 file2
      if (File.Exists(file2)) {
        string command = $"del /f /q \"{file2}\"";
        var result = ExecuteCommand(command);
        Console.WriteLine($"文件已删除: {file2}");
        Console.WriteLine(result.Output);
      } else {
        Console.WriteLine($"文件不存在: {file2}");
      }

      // 检查并删除计划任务
      string taskQueryCommand = $"schtasks /query /tn \"{taskName}\"";
      var taskQueryResult = ExecuteCommand(taskQueryCommand);
      if (taskQueryResult.ExitCode == 0) {
        string deleteTaskCommand = $"schtasks /delete /tn \"{taskName}\" /f";
        var deleteTaskResult = ExecuteCommand(deleteTaskCommand);
        Console.WriteLine("已成功删除计划任务 \"Omen Boot\"。");
        Console.WriteLine(deleteTaskResult.Output);
      } else {
        Console.WriteLine($"计划任务 \"{taskName}\" 不存在。");
      }

      // 从注册表中删除开机自启项
      string regDeleteCommand = @"reg delete ""HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"" /v ""OmenSuperHub"" /f";
      var regDeleteResult = ExecuteCommand(regDeleteCommand);
      Console.WriteLine("成功取消开机自启");
      Console.WriteLine(regDeleteResult.Output);
    }

    // Initialize tray icon
    static void InitTrayIcon() {
      try {
        AppSettingsSnapshot snapshot;
        if (settingsService.TryLoadConfig(out snapshot)) {
          customIcon = snapshot.CustomIcon;
          if (customIcon == "custom" && !shellService.HasCustomIconFile(AppDomain.CurrentDomain.BaseDirectory)) {
            customIcon = "original";
            SaveConfig("CustomIcon");
            UpdateCheckedState("CustomIcon", "原版");
          }
        }
      } catch (Exception ex) {
        Console.WriteLine($"Error restoring configuration: {ex.Message}");
      }

      shellService.Initialize(OnShellTick, ShowMainWindow, Exit);
      RefreshShellStatus();
    }

    static void RestoreCPUPower() {
      // 恢复CPU功耗设定
      if (cpuPower == "max") {
        SetCpuPowerLimit(254);
      } else if (cpuPower.Contains(" W")) {
        int value = int.Parse(cpuPower.Replace(" W", "").Trim());
        if (value > 10 && value <= 254) {
          SetCpuPowerLimit((byte)value);
        }
      }
    }

    static void RestoreGpuPower() {
      switch (gpuPower) {
        case "max":
          SetMaxGpuPower();
          break;
        case "med":
          SetMedGpuPower();
          break;
        default:
          SetMinGpuPower();
          break;
      }
    }

    static int GetManualCpuLimitWattsForController() {
      if (cpuPower == "max")
        return 125;

      if (cpuPower.EndsWith(" W")) {
        int value;
        if (int.TryParse(cpuPower.Replace(" W", string.Empty).Trim(), out value))
          return Math.Max(25, Math.Min(254, value));
      }

      return 90;
    }

    static GpuPowerTier GetManualGpuTierForController() {
      switch (gpuPower) {
        case "max":
          return GpuPowerTier.Max;
        case "med":
          return GpuPowerTier.Med;
        default:
          return GpuPowerTier.Min;
      }
    }

    static string FormatGpuTierForDisplay(GpuPowerTier tier) {
      switch (tier) {
        case GpuPowerTier.Max:
          return "max";
        case GpuPowerTier.Med:
          return "med";
        default:
          return "min";
      }
    }

    static void ApplySmartPowerControl() {
      if (!smartPowerControlEnabled || isShuttingDown || countDB > 0 || DBVersion == 1)
        return;

      lock (powerControlLock) {
        try {
          float? batteryDischarge = null;
          if (currentBatteryTelemetry != null &&
              currentBatteryTelemetry.Discharging &&
              currentBatteryTelemetry.DischargeRateMilliwatts > 0) {
            batteryDischarge = currentBatteryTelemetry.DischargeRateMilliwatts / 1000f;
          }

          List<TemperatureSensorReading> temperatureSensors = GetTemperatureSensorSnapshot();
          string cpuSensorSource;
          string gpuSensorSource;
          float cpuControlTemp = hardwareTelemetryService.SelectControlTemperature(true, temperatureSensors, CPUTemp, out cpuSensorSource);
          float gpuControlTemp = hardwareTelemetryService.SelectControlTemperature(false, temperatureSensors, GPUTemp, out gpuSensorSource);
          controlCpuTemperatureC = cpuControlTemp;
          controlGpuTemperatureC = gpuControlTemp;
          controlCpuSensorName = cpuSensorSource;
          controlGpuSensorName = gpuSensorSource;

          var input = new PowerControlInput {
            AcOnline = powerOnline,
            PerformanceMode = fanMode == "performance",
            CoolFanCurve = fanTable == "cool",
            MonitorGpu = monitorGPU,
            FanControlAuto = fanControl == "auto",
            ManualCpuLimitWatts = GetManualCpuLimitWattsForController(),
            ManualGpuTier = GetManualGpuTierForController(),
            CpuTemperatureC = cpuControlTemp,
            CpuPowerWatts = CPUPower,
            GpuTemperatureC = gpuControlTemp,
            GpuPowerWatts = GPUPower,
            BaseSystemPowerWatts = powerOnline ? (monitorGPU ? 14f : 11f) : (monitorGPU ? 10f : 8f),
            BatteryDischargePowerWatts = batteryDischarge,
            BatteryPercent = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100f)
          };

          PowerControlDecision decision = powerController.Evaluate(input);
          smartPowerControlState = decision.State;
          smartPowerControlReason = decision.Reason;
          controlCpuTempWallC = decision.CpuTempWallC;
          controlGpuTempWallC = decision.GpuTempWallC;
          controlThermalFeedback = decision.ThermalFeedback;
          estimatedSystemPowerWatts = decision.EstimatedSystemPowerWatts;
          targetSystemPowerWatts = decision.TargetSystemPowerWatts;
          smartCpuLimitWatts = decision.CurrentCpuLimitWatts;
          smartGpuTier = FormatGpuTierForDisplay(decision.CurrentGpuTier);
          smartFanBoostActive = decision.FanBoostActive;

          if (decision.ApplyCpuLimit) {
            int cpuLimit = Math.Max(1, Math.Min(254, decision.CpuLimitWatts));
            SetCpuPowerLimit((byte)cpuLimit);
          }

          if (decision.ApplyGpuTier) {
            switch (decision.GpuTier) {
              case GpuPowerTier.Max:
                SetMaxGpuPower();
                break;
              case GpuPowerTier.Med:
                SetMedGpuPower();
                break;
              default:
                SetMinGpuPower();
                break;
            }
          }

          if (fanControl == "auto" && decision.ApplyFanBoost) {
            if (decision.FanBoostActive) {
              SetMaxFanSpeedOn();
            } else {
              SetMaxFanSpeedOff();
            }
          }
        } catch {
        }
      }
    }

    static bool CheckCustomIcon() {
      if (shellService.HasCustomIconFile(AppDomain.CurrentDomain.BaseDirectory)) {
        return true;
      }
      MessageBox.Show("不存在自定义图标custom.ico", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
      return false;
    }

    // 获取显卡数字代号
    static string GetNVIDIAModel() {
      // 执行 nvidia-smi 命令并获取输出
      var result = ExecuteCommand("nvidia-smi --query-gpu=name --format=csv");

      // 检查命令是否成功执行
      if (result.ExitCode == 0) {

        string gpuModel;

        string output = result.Output;

        string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string modelName = null;
        // 检查是否有至少两行
        if (lines.Length > 1) {
          modelName = lines[1]; // 返回第二行
        }

        // 定义正则表达式以匹配第一个以数字开头的部分
        string pattern = @"\b(\d[\w\d\-]*)\b";

        // 查找第一个匹配项
        var match = Regex.Match(output, pattern);
        if (match.Success) {
          gpuModel = match.Groups[1].Value; // 返回匹配到的代号部分
          //if(modelName != null)
          //  MessageBox.Show($"显卡型号为：{gpuModel}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          Console.WriteLine($"First GPU Model Code: {gpuModel}");
          return gpuModel;
        } else {
          Console.WriteLine("GPU model code not found.");
        }
      } else {
        Console.WriteLine($"Error executing command: {result.Error}");
      }

      return null;
    }

    // 设置显卡频率限制
    static bool SetGPUClockLimit(int freq) {
      if (freq < 210) {
        ExecuteCommand("nvidia-smi --reset-gpu-clocks");
        return false;
      } else {
        ExecuteCommand("nvidia-smi --lock-gpu-clocks=0," + freq);
        return true;
      }
    }

    // 判断是否为最大显卡功耗并得到当前显卡功耗限制
    // 若限制超过1W则输出当前显卡功耗限制，否则输出为负数
    static float GPUPowerLimits() {
      // state为“当前显卡功耗限制”或“显卡功耗限制已锁定”
      string output = ExecuteCommand("nvidia-smi -q -d POWER").Output;
      // 定义正则表达式模式以提取当前功率限制和最大功率限制
      string currentPowerLimitPattern = @"Current Power Limit\s+:\s+([\d.]+)\s+W";
      string maxPowerLimitPattern = @"Max Power Limit\s+:\s+([\d.]+)\s+W";

      // 查找当前功率限制和最大功率限制的匹配项
      var currentPowerLimitMatch = Regex.Match(output, currentPowerLimitPattern);
      var maxPowerLimitMatch = Regex.Match(output, maxPowerLimitPattern);

      // 检查匹配是否成功
      if (currentPowerLimitMatch.Success && maxPowerLimitMatch.Success) {
        // 提取值并转换为浮点数
        float currentPowerLimit = float.Parse(currentPowerLimitMatch.Groups[1].Value);
        float maxPowerLimit = float.Parse(maxPowerLimitMatch.Groups[1].Value);

        // 比较值并返回结果
        if (Math.Abs(currentPowerLimit - maxPowerLimit) < 1f) // 对于浮点数比较的容差
          return -currentPowerLimit;

        else {
          return currentPowerLimit;
        }
      } else {
        // 无法找到所有所需的功率限制
        Console.WriteLine("Error: Unable to find both power limits in the output.");
        return -2;
      }
    }

    static bool CheckDBVersion(int kind) {
      ProcessResult result = ExecuteCommand("nvidia-smi");

      if (result.ExitCode == 0) {
        string pattern = @"Driver Version:\s*(\d+\.\d+)";
        Match match = Regex.Match(result.Output, pattern);
        string version = match.Success ? match.Groups[1].Value : null;

        if (version != null) {
          Version v1 = new Version(version);
          Version v2 = new Version("537.42");
          //if(kind == 2)
          //  v2 = new Version("555.99");
          if (v1.CompareTo(v2) >= 0) {
            //MessageBox.Show("当前显卡驱动：" + version, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
          } else {
            MessageBox.Show("请安装新版显卡驱动", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
          }
        } else {
          MessageBox.Show($"无法找到 NVIDIA 显卡驱动版本", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
          return false;
        }
      } else {
        MessageBox.Show($"查询显卡驱动失败", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
      }
    }

    static void ChangeDBVersion(int kind) {
      string infFileName = "nvpcf.inf";
      string currentPath = AppDomain.CurrentDomain.BaseDirectory;

      // 提取资源中的nvpcf文件到当前目录
      string extractedInfFilePath = Path.Combine(currentPath, "nvpcf.inf");
      string extractedSysFilePath = Path.Combine(currentPath, "nvpcf.sys");
      string extractedCatFilePath = Path.Combine(currentPath, "nvpcf.CAT");

      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_inf.inf", extractedInfFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_sys.sys", extractedSysFilePath);
      ExtractResourceToFile("OmenSuperHub.Resources.nvpcf_cat.CAT", extractedCatFilePath);

      string targetVersion = "08/28/2023 31.0.15.3730";
      string driverFile = Path.Combine(currentPath, "nvpcf.inf");
      //if (kind == 2) {
      //  targetVersion = "03/02/2024, 32.0.15.5546";
      //  driverFile = Path.Combine(currentPath, "nvpcf.inf_560.70", "nvpcf.inf");
      //}

      bool hasVersion = false;

      //string tempFilePath = Path.Combine(Path.GetTempPath(), "pnputil_output.txt");
      //string command = $"pnputil /enum-drivers > \"{tempFilePath}\"";
      //ExecuteCommand(command);
      //string output = File.ReadAllText(tempFilePath);
      //// 读取驱动程序列表文件
      //var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

      string command = "pnputil /enum-drivers";
      var result = ExecuteCommand(command);
      string output = result.Output;

      // 读取驱动程序列表文件
      var lines = output.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      //try {
      //  File.WriteAllLines(Path.Combine(currentPath, "driver.txt"), lines);
      //} catch (Exception ex) {
      //  Console.WriteLine($"Error: {ex.Message}");
      //}

      // 记录需要删除的 Published Name
      var namesToDelete = new List<string>();
      for (int i = 0; i < lines.Length; i++) {
        if (lines[i].Contains($":      {infFileName}")) {
          // 记录上一行的 Published Name
          if (i > 0 && lines[i - 1].Contains(":")) {
            string publishedName = lines[i - 1].Split(':')[1].Trim();

            // 记录 +4 行的 Driver Version
            if (i + 4 < lines.Length && lines[i + 4].Contains(":")) {
              string driverVersion = lines[i + 4].Split(':')[1].Trim();

              if (driverVersion != targetVersion) {
                Console.WriteLine("发现其他版本: " + driverVersion);
                namesToDelete.Add(publishedName);
              } else {
                hasVersion = true;
                Console.WriteLine("已经存在所需版本!");
              }
            }
          }
        }
      }

      if (!hasVersion) {
        ExecuteCommand($"pnputil /add-driver \"{driverFile}\" /install /force");
        Console.WriteLine("成功更改DB版本!");
      }

      if (namesToDelete.Count > 0) {
        Console.WriteLine("找到需要删除的驱动程序包:");
        foreach (var name in namesToDelete) {
          Console.WriteLine($"删除驱动程序包: {name}");
          ExecuteCommand($"pnputil /delete-driver \"{name}\" /uninstall /force");
        }
      } else {
        Console.WriteLine("没有需要删除的驱动程序包.");
      }

      // 清理临时文件
      //File.Delete(driversListFile);

      // 删除提取的nvpcf文件
      DeleteExtractedFiles(extractedInfFilePath);
      DeleteExtractedFiles(extractedSysFilePath);
      DeleteExtractedFiles(extractedCatFilePath);

      Console.WriteLine("操作完成.");
      Console.ReadLine();
    }

    static void ExtractResourceToFile(string resourceName, string outputFilePath) {
      using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)) {
        if (resourceStream != null) {
          using (FileStream fileStream = new FileStream(outputFilePath, FileMode.Create)) {
            resourceStream.CopyTo(fileStream);
          }
          Console.WriteLine($"资源文件已提取到: {outputFilePath}");
        } else {
          Console.WriteLine($"无法找到资源: {resourceName}");
        }
      }
    }

    static void DeleteExtractedFiles(string filePath) {
      // 删除提取的文件
      if (File.Exists(filePath)) {
        File.Delete(filePath);
        Console.WriteLine($"删除临时文件:{filePath}");
      }
    }

    static ProcessResult ExecuteCommand(string command) {
      return processCommandService.Execute(command);
    }

    static void UpdateCheckedState(string group, string itemText = null, ToolStripMenuItem menuItemToCheck = null) {
      shellService.UpdateCheckedState(group, itemText, menuItemToCheck);
    }

    static void OnShellTick() {
      if (isShuttingDown) {
        return;
      }

      RefreshShellStatus();

      // 启用再禁用DB驱动
      if (countDB > 0) {
        countDB--;
        if (countDB == 0) {
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /disable-device {deviceId}";
          ExecuteCommand(command);

          float powerLimits = GPUPowerLimits();
          // 检查显卡当前功耗限制，离电时当作解锁成功
          if (powerOnline && powerLimits >= 0) {
            tryTimes++;
            // 失败时重试一次
            if (tryTimes == 2) {
              tryTimes = 0;
              if (CPUPower > CPULimitDB + 10)
                MessageBox.Show($"请在CPU低负载下解锁", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              else
                MessageBox.Show($"功耗异常，解锁失败，请重新尝试！\n当前显卡功耗限制为：{powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
              command = $"pnputil /enable-device {deviceId}";
              ExecuteCommand(command);
              DBVersion = 2;
              countDB = 0;
              SaveConfig("DBVersion");
              UpdateCheckedState("DBGroup", "普通版本");
            } else {
              SetFanMode(0x31);
              SetMaxGpuPower();
              SetCpuPowerLimit((byte)CPULimitDB);
              countDB = countDBInit;
            }
          } else {
            tryTimes = 0;
            if (autoStart == "off") {
              MessageBox.Show($"解锁成功！但当前未设置开机自启，解锁后若重启电脑会导致功耗异常，需要重新解锁！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            //MessageBox.Show($"解锁成功！\n当前最大显卡功耗锁定为：{-powerLimits:F2} W ！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
          }
          if (tryTimes == 0) {
            // 恢复模式设定
            if (fanMode.Contains("performance")) {
              SetFanMode(0x31);
            } else if (fanMode.Contains("default")) {
              SetFanMode(0x30);
            }

            // 恢复CPU功耗设定
            RestoreCPUPower();
          }
        } else if (countDB == countDBInit - 1) {
          // 启用DB驱动
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
        }
      }

      // 从休眠中启动后恢复配置
      if (countRestore > 0) {
        countRestore--;
        if (countRestore == 0) {
          RestoreConfig();
        }
      }
    }

    static void QueryHarware() {
      HardwareTelemetrySnapshot snapshot = hardwareTelemetryService.Poll(new HardwareTelemetryRequest {
        CurrentCpuTemperature = CPUTemp,
        CurrentGpuTemperature = GPUTemp,
        CurrentCpuPowerWatts = CPUPower,
        CurrentGpuPowerWatts = GPUPower,
        RespondSpeed = respondSpeed,
        MonitorGpu = monitorGPU
      });

      CPUTemp = snapshot.CpuTemperature;
      GPUTemp = snapshot.GpuTemperature;
      CPUPower = snapshot.CpuPowerWatts;
      GPUPower = snapshot.GpuPowerWatts;
      currentGfxMode = snapshot.GraphicsMode;
      currentGpuStatus = snapshot.GpuStatus;
      currentSystemDesignData = snapshot.SystemDesignData;
      currentSmartAdapterStatus = snapshot.SmartAdapterStatus;
      currentFanTypeInfo = snapshot.FanTypeInfo;
      currentKeyboardType = snapshot.KeyboardType;
      currentBatteryTelemetry = snapshot.BatteryTelemetry;
      lock (temperatureSensorsLock) {
        currentTemperatureSensors = snapshot.TemperatureSensors ?? new List<TemperatureSensorReading>();
      }
    }

    static float? GetBatteryPowerWatts(BatteryTelemetry telemetry) {
      return HardwareTelemetryService.GetBatteryPowerWatts(telemetry);
    }

    internal static DashboardSnapshot GetDashboardSnapshot() {
      return new DashboardSnapshot {
        CpuTemperature = CPUTemp,
        GpuTemperature = GPUTemp,
        CpuPowerWatts = CPUPower,
        GpuPowerWatts = GPUPower,
        FanSpeeds = new List<int>(fanSpeedNow),
        MonitorGpu = monitorGPU,
        MonitorFan = monitorFan,
        AcOnline = powerOnline,
        FanMode = fanMode,
        FanControl = fanControl,
        FanTable = fanTable,
        TempSensitivity = tempSensitivity,
        CpuPowerSetting = cpuPower,
        GpuPowerSetting = gpuPower,
        GpuClockLimit = gpuClock,
        FloatingBarEnabled = floatingBar == "on",
        FloatingBarLocation = floatingBarLoc,
        GraphicsMode = currentGfxMode,
        GpuStatus = currentGpuStatus == null ? null : new OmenGpuStatus {
          CustomTgpEnabled = currentGpuStatus.CustomTgpEnabled,
          PpabEnabled = currentGpuStatus.PpabEnabled,
          DState = currentGpuStatus.DState,
          ThermalThreshold = currentGpuStatus.ThermalThreshold,
          RawData = currentGpuStatus.RawData == null ? null : (byte[])currentGpuStatus.RawData.Clone()
        },
        SystemDesignData = currentSystemDesignData == null ? null : new OmenSystemDesignData {
          PowerFlags = currentSystemDesignData.PowerFlags,
          ThermalPolicyVersion = currentSystemDesignData.ThermalPolicyVersion,
          FeatureFlags = currentSystemDesignData.FeatureFlags,
          DefaultPl4 = currentSystemDesignData.DefaultPl4,
          BiosOverclockingSupport = currentSystemDesignData.BiosOverclockingSupport,
          MiscFlags = currentSystemDesignData.MiscFlags,
          DefaultConcurrentTdp = currentSystemDesignData.DefaultConcurrentTdp,
          SoftwareFanControlSupported = currentSystemDesignData.SoftwareFanControlSupported,
          ExtremeModeSupported = currentSystemDesignData.ExtremeModeSupported,
          ExtremeModeUnlocked = currentSystemDesignData.ExtremeModeUnlocked,
          GraphicsSwitcherSupported = currentSystemDesignData.GraphicsSwitcherSupported,
          RawData = currentSystemDesignData.RawData == null ? null : (byte[])currentSystemDesignData.RawData.Clone()
        },
        SmartAdapterStatus = currentSmartAdapterStatus,
        FanTypeInfo = currentFanTypeInfo == null ? null : new OmenFanTypeInfo {
          RawValue = currentFanTypeInfo.RawValue,
          Fan1Type = currentFanTypeInfo.Fan1Type,
          Fan2Type = currentFanTypeInfo.Fan2Type
        },
        KeyboardType = currentKeyboardType,
        Battery = currentBatteryTelemetry == null ? null : new BatteryTelemetry {
          PowerOnline = currentBatteryTelemetry.PowerOnline,
          Charging = currentBatteryTelemetry.Charging,
          Discharging = currentBatteryTelemetry.Discharging,
          DischargeRateMilliwatts = currentBatteryTelemetry.DischargeRateMilliwatts,
          ChargeRateMilliwatts = currentBatteryTelemetry.ChargeRateMilliwatts,
          RemainingCapacityMilliwattHours = currentBatteryTelemetry.RemainingCapacityMilliwattHours,
          VoltageMillivolts = currentBatteryTelemetry.VoltageMillivolts
        },
        BatteryPercent = (int)Math.Round(SystemInformation.PowerStatus.BatteryLifePercent * 100),
        SmartPowerControlEnabled = smartPowerControlEnabled,
        SmartPowerControlState = smartPowerControlState,
        SmartPowerControlReason = smartPowerControlReason,
        ControlCpuTemperature = controlCpuTemperatureC,
        ControlGpuTemperature = controlGpuTemperatureC,
        ControlCpuSensor = controlCpuSensorName,
        ControlGpuSensor = controlGpuSensorName,
        ControlCpuTempWall = controlCpuTempWallC,
        ControlGpuTempWall = controlGpuTempWallC,
        ControlThermalFeedback = controlThermalFeedback,
        EstimatedSystemPowerWatts = estimatedSystemPowerWatts,
        TargetSystemPowerWatts = targetSystemPowerWatts,
        SmartCpuLimitWatts = smartCpuLimitWatts,
        SmartGpuTier = smartGpuTier,
        SmartFanBoostActive = smartFanBoostActive,
        TemperatureSensors = GetTemperatureSensorSnapshot()
      };
    }

    static List<TemperatureSensorReading> GetTemperatureSensorSnapshot() {
      lock (temperatureSensorsLock) {
        var snapshot = new List<TemperatureSensorReading>(currentTemperatureSensors.Count);
        foreach (var reading in currentTemperatureSensors) {
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

    static void ShowMainWindow() {
      MainForm.Instance.Show();
      MainForm.Instance.WindowState = FormWindowState.Normal;
      MainForm.Instance.BringToFront();
      MainForm.Instance.Activate();
    }

    static void RefreshShellStatus() {
      shellService.RefreshStatus(CreateAppShellStatus());
    }

    static AppShellStatus CreateAppShellStatus() {
      return new AppShellStatus {
        IconMode = customIcon,
        TrayText = BuildTraySummaryText(),
        DynamicIconValue = (int)CPUTemp,
        CustomIconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom.ico"),
        FloatingVisible = floatingBar == "on",
        FloatingText = BuildMonitorText(),
        FloatingTextSize = textSize,
        FloatingLocation = floatingBarLoc,
        MainWindowVisible = MainForm.IsVisibleOnScreen
      };
    }

    static string FormatGfxMode(OmenGfxMode mode) {
      switch (mode) {
        case OmenGfxMode.Hybrid:
          return "Hybrid";
        case OmenGfxMode.Discrete:
          return "Discrete";
        case OmenGfxMode.Optimus:
          return "Optimus";
        default:
          return "Unknown";
      }
    }

    static string FormatGpuControl(OmenGpuStatus status) {
      if (status == null)
        return "Unknown";

      string powerMode;
      if (status.CustomTgpEnabled && status.PpabEnabled)
        powerMode = "cTGP+PPAB";
      else if (status.CustomTgpEnabled)
        powerMode = "cTGP";
      else
        powerMode = "BaseTGP";

      return $"{powerMode} D{status.DState}";
    }

    static string FormatAdapterStatus(OmenSmartAdapterStatus status) {
      switch (status) {
        case OmenSmartAdapterStatus.MeetsRequirement:
          return "OK";
        case OmenSmartAdapterStatus.BatteryPower:
          return "Battery";
        case OmenSmartAdapterStatus.BelowRequirement:
          return "Low";
        case OmenSmartAdapterStatus.NotFunctioning:
          return "Fault";
        case OmenSmartAdapterStatus.NoSupport:
          return "N/A";
        default:
          return "?";
      }
    }

    static string FormatFanTypes(OmenFanTypeInfo fanTypeInfo) {
      if (fanTypeInfo == null)
        return null;

      return $"{fanTypeInfo.Fan1Type}/{fanTypeInfo.Fan2Type}";
    }

    static string BuildTraySummaryText() {
      List<string> parts = new List<string>();
      parts.Add($"CPU {CPUTemp:F0}C {CPUPower:F0}W");

      if (monitorGPU)
        parts.Add($"GPU {GPUTemp:F0}C {GPUPower:F0}W");

      float? batteryWatts = GetBatteryPowerWatts(currentBatteryTelemetry);
      if (batteryWatts.HasValue && !powerOnline)
        parts.Add($"BAT {batteryWatts.Value:F0}W");

      if (currentGfxMode != OmenGfxMode.Unknown)
        parts.Add(FormatGfxMode(currentGfxMode));

      string text = string.Join(" | ", parts);
      if (text.Length > 63)
        text = text.Substring(0, 63);
      return text;
    }

    static void LoadFanConfig(string filePath) {
      fanCurveService.LoadConfig(filePath);
    }

    static void SavePowerControlTuning() {
      PowerControlTuning tuning;
      lock (powerControlLock) {
        tuning = powerController.GetTuningSnapshot();
      }
      settingsService.SavePowerControlTuning(tuning);
    }

    static void LoadPowerControlTuning() {
      lock (powerControlLock) {
        powerController.UpdateTuning(settingsService.LoadPowerControlTuning());
      }
    }

    static void SaveConfig(string configName = null) {
      settingsService.SaveConfig(CreateSettingsSnapshot(), configName);
    }

    static void RestoreConfig() {
      AppSettingsSnapshot snapshot;
      if (!settingsService.TryLoadConfig(out snapshot)) {
        LoadFanConfig("silent.txt");
        SetFanMode(0x31);
        SetMaxFanSpeedOff();
        SetMaxGpuPower();
        LoadPowerControlTuning();
        return;
      }

      fanTable = snapshot.FanTable;
      if (fanTable.Contains("cool")) {
        LoadFanConfig("cool.txt");
        UpdateCheckedState("fanTableGroup", "降温模式");
      } else if (fanTable.Contains("silent")) {
        LoadFanConfig("silent.txt");
        UpdateCheckedState("fanTableGroup", "安静模式");
      }

      fanMode = snapshot.FanMode;
      if (fanMode.Contains("performance")) {
        SetFanMode(0x31);
        UpdateCheckedState("fanModeGroup", "狂暴模式");
      } else if (fanMode.Contains("default")) {
        SetFanMode(0x30);
        UpdateCheckedState("fanModeGroup", "平衡模式");
      }

      fanControl = snapshot.FanControl;
      if (fanControl == "auto") {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(0, 1000);
        UpdateCheckedState("fanControlGroup", "自动");
      } else if (fanControl.Contains("max")) {
        SetMaxFanSpeedOn();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        UpdateCheckedState("fanControlGroup", "最大风扇");
      } else if (fanControl.Contains(" RPM")) {
        SetMaxFanSpeedOff();
        fanControlTimer.Change(Timeout.Infinite, Timeout.Infinite);
        ApplyManualFanRpm(fanControl);
        UpdateCheckedState("fanControlGroup", fanControl);
      }

      tempSensitivity = snapshot.TempSensitivity;
      switch (tempSensitivity) {
        case "realtime":
          respondSpeed = 1;
          UpdateCheckedState("tempSensitivityGroup", "实时");
          break;
        case "high":
          respondSpeed = 0.4f;
          UpdateCheckedState("tempSensitivityGroup", "高");
          break;
        case "medium":
          respondSpeed = 0.1f;
          UpdateCheckedState("tempSensitivityGroup", "中");
          break;
        case "low":
          respondSpeed = 0.04f;
          UpdateCheckedState("tempSensitivityGroup", "低");
          break;
      }

      cpuPower = snapshot.CpuPower;
      if (cpuPower == "max") {
        SetCpuPowerLimit(254);
        UpdateCheckedState("cpuPowerGroup", "最大");
      } else if (cpuPower.Contains(" W")) {
        int value = int.Parse(cpuPower.Replace(" W", "").Trim());
        if (value >= 5 && value <= 254) {
          SetCpuPowerLimit((byte)value);
          UpdateCheckedState("cpuPowerGroup", cpuPower);
        }
      }

      gpuPower = snapshot.GpuPower;
      switch (gpuPower) {
        case "max":
          SetMaxGpuPower();
          UpdateCheckedState("gpuPowerGroup", "CTGP开+DB开");
          break;
        case "med":
          SetMedGpuPower();
          UpdateCheckedState("gpuPowerGroup", "CTGP开+DB关");
          break;
        case "min":
          SetMinGpuPower();
          UpdateCheckedState("gpuPowerGroup", "CTGP关+DB关");
          break;
      }

      gpuClock = snapshot.GpuClock;
      if (SetGPUClockLimit(gpuClock)) {
        UpdateCheckedState("gpuClockGroup", gpuClock + " MHz");
      } else {
        UpdateCheckedState("gpuClockGroup", "还原");
      }

      DBVersion = snapshot.DBVersion;
      switch (DBVersion) {
        case 1:
          DBVersion = 1;
          SetFanMode(0x31);
          SetMaxGpuPower();
          SetCpuPowerLimit((byte)CPULimitDB);
          countDB = countDBInit;
          UpdateCheckedState("DBGroup", "解锁版本");
          break;
        case 2:
          string deviceId = "\"ACPI\\NVDA0820\\NPCF\"";
          string command = $"pnputil /enable-device {deviceId}";
          ExecuteCommand(command);
          DBVersion = 2;
          UpdateCheckedState("DBGroup", "普通版本");
          break;
      }

      autoStart = snapshot.AutoStart;
      switch (autoStart) {
        case "on":
          AutoStartEnable();
          UpdateCheckedState("autoStartGroup", "开启");
          break;
        case "off":
          UpdateCheckedState("autoStartGroup", "关闭");
          break;
      }

      alreadyRead = snapshot.AlreadyRead;

      customIcon = snapshot.CustomIcon;
      RefreshShellStatus();
      switch (customIcon) {
        case "original":
          UpdateCheckedState("customIconGroup", "原版");
          break;
        case "custom":
          UpdateCheckedState("customIconGroup", "自定义图标");
          break;
        case "dynamic":
          UpdateCheckedState("customIconGroup", "动态图标");
          break;
      }

      omenKey = snapshot.OmenKey;
      switch (omenKey) {
        case "default":
          checkFloatingTimer.Enabled = false;
          OmenKeyOff();
          OmenKeyOn(omenKey);
          UpdateCheckedState("omenKeyGroup", "默认");
          break;
        case "custom":
          checkFloatingTimer.Enabled = true;
          OmenKeyOff();
          OmenKeyOn(omenKey);
          UpdateCheckedState("omenKeyGroup", "切换浮窗显示");
          break;
        case "none":
          checkFloatingTimer.Enabled = false;
          OmenKeyOff();
          UpdateCheckedState("omenKeyGroup", "取消绑定");
          break;
      }

      libreComputer.IsGpuEnabled = true;
      monitorGPU = true;

      monitorFan = snapshot.MonitorFan;
      if (monitorFan) {
        UpdateCheckedState("monitorFanGroup", "开启风扇监控");
      } else {
        UpdateCheckedState("monitorFanGroup", "关闭风扇监控");
      }

      smartPowerControlEnabled = snapshot.SmartPowerControlEnabled;
      if (!smartPowerControlEnabled) {
        powerController.Reset();
        smartPowerControlState = "manual";
        smartPowerControlReason = "disabled";
        smartFanBoostActive = false;
      }
      LoadPowerControlTuning();

      textSize = snapshot.FloatingBarSize;
      RefreshShellStatus();
      switch (textSize) {
        case 24:
          UpdateCheckedState("floatingBarSizeGroup", "24号");
          break;
        case 36:
          UpdateCheckedState("floatingBarSizeGroup", "36号");
          break;
        case 48:
          UpdateCheckedState("floatingBarSizeGroup", "48号");
          break;
      }

      floatingBarLoc = snapshot.FloatingBarLocation;
      RefreshShellStatus();
      if (floatingBarLoc == "left") {
        UpdateCheckedState("floatingBarLocGroup", "左上角");
      } else {
        UpdateCheckedState("floatingBarLocGroup", "右上角");
      }

      floatingBar = snapshot.FloatingBar;
      RefreshShellStatus();
      if (floatingBar == "on") {
        UpdateCheckedState("floatingBarGroup", "显示浮窗");
      } else {
        UpdateCheckedState("floatingBarGroup", "关闭浮窗");
      }
    }

    static AppSettingsSnapshot CreateSettingsSnapshot() {
      return new AppSettingsSnapshot {
        FanTable = fanTable,
        FanMode = fanMode,
        FanControl = fanControl,
        TempSensitivity = tempSensitivity,
        CpuPower = cpuPower,
        GpuPower = gpuPower,
        GpuClock = gpuClock,
        DBVersion = DBVersion,
        AutoStart = autoStart,
        AlreadyRead = alreadyRead,
        CustomIcon = customIcon,
        OmenKey = omenKey,
        MonitorFan = monitorFan,
        SmartPowerControlEnabled = smartPowerControlEnabled,
        FloatingBarSize = textSize,
        FloatingBarLocation = floatingBarLoc,
        FloatingBar = floatingBar
      };
    }

    static void HandleFloatingBarToggle() {
      if (isShuttingDown) {
        return;
      }

      if (checkFloating) {
        checkFloating = false;
        if (floatingBar == "on") {
          floatingBar = "off";
          UpdateCheckedState("floatingBarGroup", "关闭浮窗");
        } else {
          floatingBar = "on";
          UpdateCheckedState("floatingBarGroup", "显示浮窗");
        }
        RefreshShellStatus();
        SaveConfig("FloatingBar");
      }
    }

    static void getOmenKeyTask() {
      omenKeyListenerTask = TaskEx.Run(() => {
        while (!isShuttingDown) {
          try {
            using (var pipeServer = new NamedPipeServerStream("OmenSuperHubPipe", PipeDirection.In)) {
              omenKeyPipeServer = pipeServer;
              pipeServer.WaitForConnection();
              if (isShuttingDown) {
                break;
              }

              using (var reader = new StreamReader(pipeServer)) {
                string message = reader.ReadToEnd();
                if (!string.IsNullOrEmpty(message) && message.Contains("OmenKeyTriggered")) {
                  checkFloating = true;
                }
              }
            }
          } catch (ObjectDisposedException) {
            break;
          } catch (IOException) {
            if (isShuttingDown) {
              break;
            }
          } finally {
            omenKeyPipeServer = null;
          }
        }
      });
    }

    static string BuildMonitorText() {
      List<string> lines = new List<string>();
      lines.Add($"CPU: {CPUTemp:F1}°C  {CPUPower:F1}W");

      if (monitorGPU)
        lines.Add($"GPU: {GPUTemp:F1}°C  {GPUPower:F1}W");

      float systemPower = CPUPower + (monitorGPU ? GPUPower : 0f);
      string source = powerOnline ? "AC" : "BAT";
      if (!powerOnline && currentBatteryTelemetry != null) {
        float? batteryWatts = GetBatteryPowerWatts(currentBatteryTelemetry);
        if (batteryWatts.HasValue) {
          systemPower = batteryWatts.Value;
        }
      }
      lines.Add($"SYS: {systemPower:F1}W ({source})");

      if (monitorFan)
        lines.Add($"FAN: {fanSpeedNow[0] * 100}/{fanSpeedNow[1] * 100} RPM");

      return string.Join("\n", lines);
    }

    static void Exit() {
      if (Interlocked.Exchange(ref shutdownStarted, 1) != 0) {
        return;
      }

      isShuttingDown = true;
      if (omenKey == "custom") {
        OmenKeyOff();
      }

      SystemEvents.PowerModeChanged -= new PowerModeChangedEventHandler(OnPowerChange);
      StopAndDisposeTimers();
      DisposePipeServer();
      shellService.Dispose();
      libreComputer.Close();
      Application.Exit();
    }

    static void OnApplicationExit(object sender, EventArgs e) {
      if (Interlocked.Exchange(ref shutdownStarted, 1) != 0) {
        return;
      }

      isShuttingDown = true;
      SystemEvents.PowerModeChanged -= new PowerModeChangedEventHandler(OnPowerChange);
      StopAndDisposeTimers();
      DisposePipeServer();
      shellService.Dispose();

      libreComputer.Close();
    }

    static void StopAndDisposeTimers() {
      if (hardwarePollingTimer != null) {
        hardwarePollingTimer.Dispose();
        hardwarePollingTimer = null;
      }

      if (checkFloatingTimer != null) {
        checkFloatingTimer.Stop();
        checkFloatingTimer.Dispose();
        checkFloatingTimer = null;
      }

      if (optimiseTimer != null) {
        optimiseTimer.Stop();
        optimiseTimer.Dispose();
        optimiseTimer = null;
      }

      if (fanControlTimer != null) {
        fanControlTimer.Dispose();
        fanControlTimer = null;
      }
    }

    static void DisposePipeServer() {
      var pipeServer = omenKeyPipeServer;
      omenKeyPipeServer = null;
      if (pipeServer != null) {
        pipeServer.Dispose();
      }
    }

    static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
      Exception ex = (Exception)e.ExceptionObject;
      LogError(ex);
    }

    static void Application_ThreadException(object sender, ThreadExceptionEventArgs e) {
      Exception ex = e.Exception;
      LogError(ex);
    }

    static void LogError(Exception ex) {
      try {
        string absoluteFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
        File.AppendAllText(absoluteFilePath, DateTime.Now + ": " + ex.ToString() + Environment.NewLine);
      } catch {
      }

      if (!isShuttingDown) {
        MessageBox.Show("An unexpected error occurred. Please check the log file for details.");
      }
    }
  }
}
