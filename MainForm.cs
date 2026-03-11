using System;
using System.Drawing;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class MainForm : Form {
    private sealed class BufferedPanel : Panel {
      public BufferedPanel() {
        DoubleBuffered = true;
        ResizeRedraw = true;
      }
    }

    private sealed class BufferedTableLayoutPanel : TableLayoutPanel {
      public BufferedTableLayoutPanel() {
        DoubleBuffered = true;
        ResizeRedraw = true;
      }
    }

    private static MainForm _instance;

    private readonly System.Windows.Forms.Timer refreshTimer;
    private Label heroPowerLabel;
    private Label heroSourceLabel;
    private Label cpuTempLabel;
    private Label cpuPowerLabel;
    private Label gpuTempLabel;
    private Label gpuPowerLabel;
    private Label batteryLabel;
    private Label batteryDetailLabel;
    private Label statusMuxLabel;
    private Label statusAdapterLabel;
    private Label statusFanLabel;
    private Label statusPolicyLabel;
    private Label statusGpuCtlLabel;
    private Label statusCapabilitiesLabel;
    private TextBox telemetryTextBox;
    private TextBox configTextBox;
    private ProgressBar batteryProgressBar;

    public MainForm() {
      Text = "OmenSuperHub";
      StartPosition = FormStartPosition.CenterScreen;
      MinimumSize = new Size(980, 680);
      Size = new Size(1120, 760);
      BackColor = Color.FromArgb(242, 236, 226);
      Icon = Properties.Resources.fan;
      Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
      UpdateStyles();

      BuildLayout();

      refreshTimer = new System.Windows.Forms.Timer();
      refreshTimer.Interval = 1200;
      refreshTimer.Tick += (s, e) => RefreshDashboard();
      refreshTimer.Start();

      Shown += (s, e) => RefreshDashboard();
      FormClosing += MainForm_FormClosing;
    }

    private void BuildLayout() {
      SuspendLayout();

      var root = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        BackColor = BackColor,
        Padding = new Padding(20),
        ColumnCount = 1,
        RowCount = 3
      };
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

      root.Controls.Add(BuildHeader(), 0, 0);
      root.Controls.Add(BuildOverviewArea(), 0, 1);
      root.Controls.Add(BuildDetailsArea(), 0, 2);

      Controls.Add(root);
      ResumeLayout(true);
    }

    private Control BuildHeader() {
      var panel = new BufferedPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(59, 47, 38),
        Padding = new Padding(24, 20, 24, 20),
        Margin = new Padding(0, 0, 0, 14)
      };

      var titleLabel = new Label {
        AutoSize = true,
        Text = "功率与热状态面板",
        Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
        ForeColor = Color.FromArgb(250, 243, 235),
        Location = new Point(20, 18)
      };

      heroPowerLabel = new Label {
        AutoSize = true,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold),
        ForeColor = Color.FromArgb(255, 188, 97),
        Location = new Point(22, 58)
      };

      heroSourceLabel = new Label {
        AutoSize = true,
        Text = "正在收集硬件状态...",
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
        ForeColor = Color.FromArgb(216, 202, 184),
        Location = new Point(26, 92)
      };

      var refreshButton = CreateHeaderButton("立即刷新", Color.FromArgb(212, 117, 43));
      refreshButton.Click += (s, e) => RefreshDashboard();
      refreshButton.Location = new Point(820, 26);

      var hideButton = CreateHeaderButton("隐藏到托盘", Color.FromArgb(109, 92, 76));
      hideButton.Click += (s, e) => Hide();
      hideButton.Location = new Point(938, 26);

      panel.Resize += (s, e) => {
        hideButton.Left = panel.ClientSize.Width - hideButton.Width - 20;
        refreshButton.Left = hideButton.Left - refreshButton.Width - 10;
      };

      panel.Controls.Add(titleLabel);
      panel.Controls.Add(heroPowerLabel);
      panel.Controls.Add(heroSourceLabel);
      panel.Controls.Add(refreshButton);
      panel.Controls.Add(hideButton);
      return panel;
    }

    private Button CreateHeaderButton(string text, Color backColor) {
      var button = new Button {
        Text = text,
        Size = new Size(108, 34),
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = Color.White,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
      };
      button.FlatAppearance.BorderSize = 0;
      return button;
    }

    private Control BuildOverviewArea() {
      var grid = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.Transparent,
        ColumnCount = 2,
        RowCount = 1,
        Margin = new Padding(0, 0, 0, 14)
      };
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));

      grid.Controls.Add(BuildMetricPanel(), 0, 0);
      grid.Controls.Add(BuildStatusPanel(), 1, 0);
      return grid;
    }

    private Control BuildMetricPanel() {
      var grid = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 3,
        Margin = new Padding(0, 0, 10, 0),
        BackColor = Color.Transparent
      };

      for (int i = 0; i < 2; i++) {
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      }
      for (int i = 0; i < 3; i++) {
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
      }

      grid.Controls.Add(CreateMetricCard("CPU 温度", "0.0 °C", Color.FromArgb(24, 103, 160), out cpuTempLabel), 0, 0);
      grid.Controls.Add(CreateMetricCard("CPU 功率", "0.0 W", Color.FromArgb(226, 118, 50), out cpuPowerLabel), 1, 0);
      grid.Controls.Add(CreateMetricCard("GPU 温度", "0.0 °C", Color.FromArgb(35, 125, 88), out gpuTempLabel), 0, 1);
      grid.Controls.Add(CreateMetricCard("GPU 功率", "0.0 W", Color.FromArgb(121, 77, 154), out gpuPowerLabel), 1, 1);
      grid.Controls.Add(CreateBatteryCard(), 0, 2);
      grid.SetColumnSpan(grid.GetControlFromPosition(0, 2), 2);

      return grid;
    }

    private Control BuildStatusPanel() {
      var panel = CreateCardContainer(new Padding(18));
      panel.Margin = new Padding(10, 0, 0, 0);

      var titleLabel = new Label {
        AutoSize = true,
        Text = "系统状态",
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
        ForeColor = Color.FromArgb(61, 49, 39),
        Location = new Point(18, 16)
      };

      statusMuxLabel = CreateStatusLine(panel, "显卡模式", 54);
      statusAdapterLabel = CreateStatusLine(panel, "供电/适配器", 86);
      statusFanLabel = CreateStatusLine(panel, "风扇", 118);
      statusPolicyLabel = CreateStatusLine(panel, "策略", 150);
      statusGpuCtlLabel = CreateStatusLine(panel, "GPU 控制", 182);
      statusCapabilitiesLabel = CreateStatusLine(panel, "能力", 214);

      panel.Controls.Add(titleLabel);
      return panel;
    }

    private Label CreateStatusLine(Control parent, string label, int top) {
      var keyLabel = new Label {
        AutoSize = true,
        Text = label,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(130, 107, 85),
        Location = new Point(18, top)
      };

      var valueLabel = new Label {
        AutoEllipsis = true,
        Width = Math.Max(240, parent.Width - 36),
        Height = 24,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular),
        ForeColor = Color.FromArgb(48, 39, 31),
        Location = new Point(140, top - 2)
      };

      parent.Resize += (s, e) => {
        valueLabel.Width = Math.Max(180, parent.ClientSize.Width - 156);
      };

      parent.Controls.Add(keyLabel);
      parent.Controls.Add(valueLabel);
      return valueLabel;
    }

    private Control BuildDetailsArea() {
      var grid = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 1,
        BackColor = Color.Transparent
      };
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

      telemetryTextBox = CreateDetailsBox();
      configTextBox = CreateDetailsBox();

      grid.Controls.Add(CreateDetailSection("实时遥测", telemetryTextBox), 0, 0);
      grid.Controls.Add(CreateDetailSection("运行配置", configTextBox), 1, 0);

      return grid;
    }

    private BufferedPanel CreateMetricCard(string title, string initialValue, Color accentColor, out Label valueLabel) {
      var panel = CreateCardContainer(new Padding(18, 16, 18, 16));
      panel.Margin = new Padding(0, 0, 0, 10);

      var accent = new Panel {
        Dock = DockStyle.Top,
        Height = 5,
        BackColor = accentColor
      };

      var titleLabel = new Label {
        AutoSize = true,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(135, 108, 82),
        Location = new Point(18, 24)
      };

      valueLabel = new Label {
        AutoSize = true,
        Text = initialValue,
        Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
        ForeColor = Color.FromArgb(49, 39, 30),
        Location = new Point(18, 52)
      };

      panel.Controls.Add(accent);
      panel.Controls.Add(titleLabel);
      panel.Controls.Add(valueLabel);
      return panel;
    }

    private BufferedPanel CreateBatteryCard() {
      var panel = CreateCardContainer(new Padding(18, 16, 18, 16));
      panel.Margin = new Padding(0);

      var accent = new Panel {
        Dock = DockStyle.Top,
        Height = 5,
        BackColor = Color.FromArgb(184, 127, 48)
      };

      var titleLabel = new Label {
        AutoSize = true,
        Text = "电池功率与容量",
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(135, 108, 82),
        Location = new Point(18, 24)
      };

      batteryLabel = new Label {
        AutoSize = true,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
        ForeColor = Color.FromArgb(49, 39, 30),
        Location = new Point(18, 50)
      };

      batteryDetailLabel = new Label {
        AutoSize = true,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
        ForeColor = Color.FromArgb(112, 90, 68),
        Location = new Point(20, 94)
      };

      batteryProgressBar = new ProgressBar {
        Width = 300,
        Height = 18,
        Location = new Point(20, 126),
        Maximum = 100,
        Style = ProgressBarStyle.Continuous
      };

      panel.Resize += (s, e) => {
        batteryProgressBar.Width = Math.Max(180, panel.ClientSize.Width - 40);
      };

      panel.Controls.Add(accent);
      panel.Controls.Add(titleLabel);
      panel.Controls.Add(batteryLabel);
      panel.Controls.Add(batteryDetailLabel);
      panel.Controls.Add(batteryProgressBar);
      return panel;
    }

    private static BufferedPanel CreateCardContainer(Padding padding) {
      return new BufferedPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        Padding = padding
      };
    }

    private Control CreateDetailSection(string title, TextBox textBox) {
      var panel = CreateCardContainer(new Padding(18));
      panel.Margin = new Padding(0, 0, 10, 0);

      var titleLabel = new Label {
        AutoSize = true,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
        ForeColor = Color.FromArgb(61, 49, 39),
        Location = new Point(18, 16)
      };

      textBox.Location = new Point(18, 50);
      textBox.Size = new Size(Math.Max(300, panel.Width - 36), Math.Max(200, panel.Height - 68));
      panel.Resize += (s, e) => {
        textBox.Size = new Size(Math.Max(280, panel.ClientSize.Width - 36), Math.Max(180, panel.ClientSize.Height - 68));
      };

      panel.Controls.Add(titleLabel);
      panel.Controls.Add(textBox);
      return panel;
    }

    private static TextBox CreateDetailsBox() {
      return new TextBox {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(250, 247, 242),
        ForeColor = Color.FromArgb(55, 44, 34),
        Font = new Font("Consolas", 10F),
        WordWrap = false
      };
    }

    private void RefreshDashboard() {
      var snapshot = Program.GetDashboardSnapshot();
      float? batteryPower = GetBatteryPower(snapshot.Battery);
      float totalPower = snapshot.CpuPowerWatts + (snapshot.MonitorGpu ? snapshot.GpuPowerWatts : 0F);

      SetTextIfChanged(heroPowerLabel, $"{totalPower:F1} W");
      SetTextIfChanged(heroSourceLabel, $"{(snapshot.AcOnline ? "交流电" : "电池")} | CPU {snapshot.CpuPowerWatts:F1}W | GPU {(snapshot.MonitorGpu ? snapshot.GpuPowerWatts.ToString("F1") : "--")}W");
      SetTextIfChanged(cpuTempLabel, $"{snapshot.CpuTemperature:F1} °C");
      SetTextIfChanged(cpuPowerLabel, $"{snapshot.CpuPowerWatts:F1} W");
      SetTextIfChanged(gpuTempLabel, snapshot.MonitorGpu ? $"{snapshot.GpuTemperature:F1} °C" : "关闭");
      SetTextIfChanged(gpuPowerLabel, snapshot.MonitorGpu ? $"{snapshot.GpuPowerWatts:F1} W" : "--");
      SetTextIfChanged(batteryLabel, batteryPower.HasValue ? $"{batteryPower.Value:F1} W" : "无功率读数");
      SetTextIfChanged(batteryDetailLabel, BuildBatteryDetail(snapshot, batteryPower));
      batteryProgressBar.Value = Math.Max(0, Math.Min(100, snapshot.BatteryPercent));

      SetTextIfChanged(statusMuxLabel, FormatGraphicsMode(snapshot.GraphicsMode));
      SetTextIfChanged(statusAdapterLabel, $"{FormatAdapterStatus(snapshot.SmartAdapterStatus)} / {(snapshot.AcOnline ? "AC" : "Battery")}");
      SetTextIfChanged(statusFanLabel, snapshot.MonitorFan ? $"{snapshot.FanSpeeds[0] * 100}/{snapshot.FanSpeeds[1] * 100} RPM" : "监控关闭");
      SetTextIfChanged(statusPolicyLabel, $"{snapshot.FanMode} | CPU {snapshot.CpuPowerSetting} | GPU {snapshot.GpuPowerSetting}");
      SetTextIfChanged(statusGpuCtlLabel, FormatGpuControl(snapshot.GpuStatus));
      SetTextIfChanged(statusCapabilitiesLabel, BuildCapabilitySummary(snapshot));

      SetTextIfChanged(telemetryTextBox, BuildTelemetryText(snapshot, batteryPower));
      SetTextIfChanged(configTextBox, BuildConfigText(snapshot));
    }

    private static void SetTextIfChanged(Control control, string text) {
      if (control.Text != text) {
        control.Text = text;
      }
    }

    private static float? GetBatteryPower(Program.BatteryTelemetry battery) {
      if (battery == null)
        return null;

      if (battery.Discharging && battery.DischargeRateMilliwatts > 0)
        return battery.DischargeRateMilliwatts / 1000f;

      if (battery.Charging && battery.ChargeRateMilliwatts > 0)
        return battery.ChargeRateMilliwatts / 1000f;

      return null;
    }

    private static string BuildBatteryDetail(Program.DashboardSnapshot snapshot, float? batteryPower) {
      if (snapshot.Battery == null)
        return "BatteryStatus 不可用";

      string mode = snapshot.Battery.Discharging ? "放电" : (snapshot.Battery.Charging ? "充电" : (snapshot.AcOnline ? "交流电待机" : "电池待机"));
      string power = batteryPower.HasValue ? $"{batteryPower.Value:F1}W" : "--";
      string capacity = snapshot.Battery.RemainingCapacityMilliwattHours > 0 ? $"{snapshot.Battery.RemainingCapacityMilliwattHours / 1000f:F1}Wh" : "--";
      return $"{mode} | {power} | {capacity} | {snapshot.BatteryPercent}%";
    }

    private static string BuildTelemetryText(Program.DashboardSnapshot snapshot, float? batteryPower) {
      return string.Join(Environment.NewLine, new[] {
        $"CPU Temp      : {snapshot.CpuTemperature:F1} °C",
        $"CPU Power     : {snapshot.CpuPowerWatts:F1} W",
        $"GPU Temp      : {(snapshot.MonitorGpu ? snapshot.GpuTemperature.ToString("F1") + " °C" : "disabled")}",
        $"GPU Power     : {(snapshot.MonitorGpu ? snapshot.GpuPowerWatts.ToString("F1") + " W" : "--")}",
        $"Battery Power : {(batteryPower.HasValue ? batteryPower.Value.ToString("F1") + " W" : "--")}",
        $"Battery Mode  : {BuildBatteryMode(snapshot)}",
        $"Voltage       : {(snapshot.Battery == null ? "--" : (snapshot.Battery.VoltageMillivolts / 1000f).ToString("F2") + " V")}",
        $"Capacity      : {(snapshot.Battery == null ? "--" : (snapshot.Battery.RemainingCapacityMilliwattHours / 1000f).ToString("F1") + " Wh")}",
        $"Battery %     : {snapshot.BatteryPercent}%",
        $"Fan RPM       : {(snapshot.MonitorFan ? snapshot.FanSpeeds[0] * 100 + " / " + snapshot.FanSpeeds[1] * 100 : "disabled")}"
      });
    }

    private static string BuildConfigText(Program.DashboardSnapshot snapshot) {
      return string.Join(Environment.NewLine, new[] {
        $"Graphics Mode : {FormatGraphicsMode(snapshot.GraphicsMode)}",
        $"GPU Control   : {FormatGpuControl(snapshot.GpuStatus)}",
        $"Fan Control   : {snapshot.FanControl}",
        $"Fan Curve     : {snapshot.FanTable}",
        $"Perf Mode     : {snapshot.FanMode}",
        $"CPU Limit     : {snapshot.CpuPowerSetting}",
        $"GPU Policy    : {snapshot.GpuPowerSetting}",
        $"GPU Clock     : {(snapshot.GpuClockLimit > 0 ? snapshot.GpuClockLimit + " MHz" : "restore")}",
        $"Adapter       : {FormatAdapterStatus(snapshot.SmartAdapterStatus)}",
        $"Capabilities  : {BuildCapabilitySummary(snapshot)}",
        $"Keyboard      : {(byte)snapshot.KeyboardType:X2}",
        $"Fan Type      : {(snapshot.FanTypeInfo == null ? "--" : snapshot.FanTypeInfo.Fan1Type + "/" + snapshot.FanTypeInfo.Fan2Type)}"
      });
    }

    private static string BuildBatteryMode(Program.DashboardSnapshot snapshot) {
      if (snapshot.Battery == null)
        return "Unavailable";

      if (snapshot.Battery.Discharging)
        return "Discharging";
      if (snapshot.Battery.Charging)
        return "Charging";
      return snapshot.AcOnline ? "AC Idle" : "Battery Idle";
    }

    private static string FormatGraphicsMode(OmenHardware.OmenGfxMode mode) {
      switch (mode) {
        case OmenHardware.OmenGfxMode.Hybrid:
          return "Hybrid";
        case OmenHardware.OmenGfxMode.Discrete:
          return "Discrete";
        case OmenHardware.OmenGfxMode.Optimus:
          return "Optimus";
        default:
          return "Unknown";
      }
    }

    private static string FormatAdapterStatus(OmenHardware.OmenSmartAdapterStatus status) {
      switch (status) {
        case OmenHardware.OmenSmartAdapterStatus.MeetsRequirement:
          return "OK";
        case OmenHardware.OmenSmartAdapterStatus.BatteryPower:
          return "Battery";
        case OmenHardware.OmenSmartAdapterStatus.BelowRequirement:
          return "Low";
        case OmenHardware.OmenSmartAdapterStatus.NotFunctioning:
          return "Fault";
        case OmenHardware.OmenSmartAdapterStatus.NoSupport:
          return "N/A";
        default:
          return "Unknown";
      }
    }

    private static string FormatGpuControl(OmenHardware.OmenGpuStatus status) {
      if (status == null)
        return "Unknown";

      string powerMode = status.CustomTgpEnabled ? "cTGP" : "BaseTGP";
      if (status.PpabEnabled)
        powerMode += " + PPAB";
      return $"{powerMode} | D{status.DState}";
    }

    private static string BuildCapabilitySummary(Program.DashboardSnapshot snapshot) {
      if (snapshot.SystemDesignData == null)
        return "Unknown";

      string gfxSwitch = snapshot.SystemDesignData.GraphicsSwitcherSupported ? "GfxSwitch" : "No GfxSwitch";
      string fan = snapshot.SystemDesignData.SoftwareFanControlSupported ? "SW Fan" : "BIOS Fan";
      return $"{gfxSwitch} | {fan} | PL4 {snapshot.SystemDesignData.DefaultPl4}W";
    }

    private void MainForm_FormClosing(object sender, FormClosingEventArgs e) {
      if (e.CloseReason == CloseReason.UserClosing) {
        e.Cancel = true;
        Hide();
      }
    }

    public static MainForm Instance {
      get {
        if (_instance == null || _instance.IsDisposed) {
          _instance = new MainForm();
        }
        return _instance;
      }
    }
  }
}
