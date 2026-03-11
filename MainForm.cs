using System;
using System.Drawing;
using System.Windows.Forms;

namespace OmenSuperHub {
  public partial class MainForm : Form {
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;

    private sealed class BufferedTableLayoutPanel : TableLayoutPanel {
      public BufferedTableLayoutPanel() {
        DoubleBuffered = true;
        ResizeRedraw = true;
      }
    }

    private sealed class BufferedPanel : Panel {
      public BufferedPanel() {
        DoubleBuffered = true;
        ResizeRedraw = true;
      }
    }

    private static MainForm _instance;

    private readonly System.Windows.Forms.Timer refreshTimer;
    private bool suppressRefresh;

    private Label titleValueLabel;
    private Label subtitleValueLabel;
    private Label cpuTempValueLabel;
    private Label cpuPowerValueLabel;
    private Label gpuTempValueLabel;
    private Label gpuPowerValueLabel;
    private Label batteryPowerValueLabel;
    private Label batteryDetailValueLabel;
    private ProgressBar batteryProgressBar;
    private Label muxValueLabel;
    private Label adapterValueLabel;
    private Label fanValueLabel;
    private Label policyValueLabel;
    private Label gpuCtlValueLabel;
    private Label capabilityValueLabel;
    private TextBox telemetryTextBox;
    private TextBox configTextBox;

    public MainForm() {
      Text = "OmenSuperHub";
      StartPosition = FormStartPosition.CenterScreen;
      MinimumSize = new Size(980, 700);
      Size = new Size(1120, 780);
      AutoScaleMode = AutoScaleMode.Dpi;
      BackColor = Color.FromArgb(243, 239, 231);
      Icon = Properties.Resources.fan;
      Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
      UpdateStyles();

      BuildLayout();

      refreshTimer = new System.Windows.Forms.Timer();
      refreshTimer.Interval = 1500;
      refreshTimer.Tick += (s, e) => {
        if (!suppressRefresh && Visible && WindowState != FormWindowState.Minimized) {
          RefreshDashboard();
        }
      };
      refreshTimer.Start();

      Shown += (s, e) => RefreshDashboard();
      VisibleChanged += (s, e) => refreshTimer.Enabled = Visible;
      Activated += (s, e) => {
        if (Visible) {
          RefreshDashboard();
        }
      };
      FormClosing += MainForm_FormClosing;
    }

    private void BuildLayout() {
      SuspendLayout();

      var root = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        BackColor = BackColor,
        ColumnCount = 1,
        RowCount = 3,
        Padding = new Padding(20)
      };
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 176F));
      root.RowStyles.Add(new RowStyle(SizeType.Absolute, 300F));
      root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      root.Controls.Add(BuildHeader(), 0, 0);
      root.Controls.Add(BuildTopSection(), 0, 1);
      root.Controls.Add(BuildBottomSection(), 0, 2);

      Controls.Add(root);
      ResumeLayout(true);
    }

    private Control BuildHeader() {
      var panel = new BufferedPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(60, 47, 38),
        Margin = new Padding(0, 0, 0, 14),
        Padding = new Padding(18, 14, 18, 14)
      };

      var layout = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.Transparent,
        ColumnCount = 2,
        RowCount = 1
      };
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230F));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      var infoLayout = new FlowLayoutPanel {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = false,
        AutoSize = false,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = Color.Transparent
      };

      var titleLabel = new Label {
        AutoSize = true,
        Text = "功率与热状态面板",
        Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
        ForeColor = Color.FromArgb(250, 242, 234),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 8)
      };

      var buttonRow = new FlowLayoutPanel {
        Dock = DockStyle.Top,
        FlowDirection = FlowDirection.RightToLeft,
        WrapContents = false,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
        Padding = new Padding(0),
        AutoSize = true
      };

      var refreshButton = CreateHeaderButton("立即刷新", Color.FromArgb(212, 117, 43));
      refreshButton.Click += (s, e) => RefreshDashboard();

      var hideButton = CreateHeaderButton("隐藏到托盘", Color.FromArgb(113, 93, 74));
      hideButton.Click += (s, e) => Hide();

      buttonRow.Controls.Add(refreshButton);
      buttonRow.Controls.Add(hideButton);

      titleValueLabel = new Label {
        AutoSize = true,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
        ForeColor = Color.FromArgb(255, 186, 92),
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 0, 0, 8)
      };

      subtitleValueLabel = new Label {
        AutoSize = false,
        Text = "正在收集硬件状态...",
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
        ForeColor = Color.FromArgb(214, 200, 181),
        TextAlign = ContentAlignment.TopLeft,
        Margin = new Padding(0),
        Size = new Size(640, 44)
      };

      infoLayout.Resize += (s, e) => {
        subtitleValueLabel.Width = Math.Max(280, infoLayout.ClientSize.Width - 2);
        subtitleValueLabel.Height = 44;
      };

      infoLayout.Controls.Add(titleLabel);
      infoLayout.Controls.Add(titleValueLabel);
      infoLayout.Controls.Add(subtitleValueLabel);

      layout.Controls.Add(infoLayout, 0, 0);
      layout.Controls.Add(buttonRow, 1, 0);

      panel.Controls.Add(layout);

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

    private Control BuildTopSection() {
      var split = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 1,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 14)
      };
      split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
      split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));

      split.Controls.Add(BuildMetricGrid(), 0, 0);
      split.Controls.Add(BuildStatusCard(), 1, 0);
      return split;
    }

    private Control BuildMetricGrid() {
      var grid = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 3,
        Margin = new Padding(0, 0, 10, 0),
        BackColor = Color.Transparent
      };

      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
      grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
      grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      grid.Controls.Add(CreateMetricCard("CPU 温度", out cpuTempValueLabel, Color.FromArgb(26, 103, 159)), 0, 0);
      grid.Controls.Add(CreateMetricCard("CPU 功率", out cpuPowerValueLabel, Color.FromArgb(222, 118, 46)), 1, 0);
      grid.Controls.Add(CreateMetricCard("GPU 温度", out gpuTempValueLabel, Color.FromArgb(35, 123, 86)), 0, 1);
      grid.Controls.Add(CreateMetricCard("GPU 功率", out gpuPowerValueLabel, Color.FromArgb(119, 76, 152)), 1, 1);

      var batteryCard = CreateBatteryCard();
      grid.Controls.Add(batteryCard, 0, 2);
      grid.SetColumnSpan(batteryCard, 2);

      return grid;
    }

    private Control BuildStatusCard() {
      var card = CreateCard();
      card.Margin = new Padding(10, 0, 0, 0);

      var layout = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 7,
        BackColor = Color.White,
        Padding = new Padding(18, 16, 18, 16)
      };
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
      layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
      for (int i = 1; i < 7; i++) {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
      }

      var title = new Label {
        AutoSize = true,
        Text = "系统状态摘要",
        Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
        ForeColor = Color.FromArgb(62, 50, 40)
      };
      layout.Controls.Add(title, 0, 0);
      layout.SetColumnSpan(title, 2);

      muxValueLabel = AddStatusRow(layout, 1, "显卡模式");
      adapterValueLabel = AddStatusRow(layout, 2, "供电/适配器");
      fanValueLabel = AddStatusRow(layout, 3, "风扇");
      policyValueLabel = AddStatusRow(layout, 4, "策略");
      gpuCtlValueLabel = AddStatusRow(layout, 5, "GPU 控制");
      capabilityValueLabel = AddStatusRow(layout, 6, "能力");

      card.Controls.Add(layout);
      return card;
    }

    private Label AddStatusRow(TableLayoutPanel layout, int row, string key) {
      var keyLabel = new Label {
        AutoSize = true,
        Text = key,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        ForeColor = Color.FromArgb(134, 108, 83),
        Anchor = AnchorStyles.Left
      };

      var valueLabel = new Label {
        Dock = DockStyle.Fill,
        Text = "--",
        AutoEllipsis = true,
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular),
        ForeColor = Color.FromArgb(50, 39, 30),
        TextAlign = ContentAlignment.MiddleLeft
      };

      layout.Controls.Add(keyLabel, 0, row);
      layout.Controls.Add(valueLabel, 1, row);
      return valueLabel;
    }

    private Control CreateMetricCard(string title, out Label valueLabel, Color accentColor) {
      var card = CreateCard();
      card.Margin = new Padding(0, 0, 10, 10);

      var layout = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 3,
        BackColor = Color.White,
        Padding = new Padding(16, 0, 16, 12)
      };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 5F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      var accent = new Panel {
        Dock = DockStyle.Fill,
        BackColor = accentColor,
        Margin = new Padding(0)
      };

      var titleLabel = new Label {
        Dock = DockStyle.Fill,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(135, 108, 82),
        TextAlign = ContentAlignment.BottomLeft
      };

      valueLabel = new Label {
        Dock = DockStyle.Fill,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
        ForeColor = Color.FromArgb(49, 39, 30),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
      };

      layout.Controls.Add(accent, 0, 0);
      layout.Controls.Add(titleLabel, 0, 1);
      layout.Controls.Add(valueLabel, 0, 2);
      card.Controls.Add(layout);
      return card;
    }

    private Control CreateBatteryCard() {
      var card = CreateCard();
      card.Margin = new Padding(0);

      var layout = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 5,
        BackColor = Color.White,
        Padding = new Padding(16, 0, 16, 14)
      };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 5F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));

      var accent = new Panel {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(183, 127, 46),
        Margin = new Padding(0)
      };

      var titleLabel = new Label {
        Dock = DockStyle.Fill,
        Text = "电池功率与容量",
        Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
        ForeColor = Color.FromArgb(135, 108, 82),
        TextAlign = ContentAlignment.BottomLeft
      };

      batteryPowerValueLabel = new Label {
        Dock = DockStyle.Fill,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
        ForeColor = Color.FromArgb(49, 39, 30),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
      };

      batteryDetailValueLabel = new Label {
        Dock = DockStyle.Fill,
        Text = "--",
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
        ForeColor = Color.FromArgb(112, 90, 68),
        TextAlign = ContentAlignment.MiddleLeft,
        AutoEllipsis = true
      };

      batteryProgressBar = new ProgressBar {
        Dock = DockStyle.Fill,
        Maximum = 100,
        Style = ProgressBarStyle.Continuous,
        Margin = new Padding(0, 2, 0, 0)
      };

      layout.Controls.Add(accent, 0, 0);
      layout.Controls.Add(titleLabel, 0, 1);
      layout.Controls.Add(batteryPowerValueLabel, 0, 2);
      layout.Controls.Add(batteryDetailValueLabel, 0, 3);
      layout.Controls.Add(batteryProgressBar, 0, 4);
      card.Controls.Add(layout);
      return card;
    }

    private Control BuildBottomSection() {
      var split = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 1,
        BackColor = Color.Transparent
      };
      split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
      split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

      telemetryTextBox = CreateDetailsBox();
      configTextBox = CreateDetailsBox();

      var telemetryCard = CreateDetailsCard("实时遥测", telemetryTextBox);
      telemetryCard.Margin = new Padding(0, 0, 10, 0);
      var configCard = CreateDetailsCard("运行配置", configTextBox);
      configCard.Margin = new Padding(10, 0, 0, 0);

      split.Controls.Add(telemetryCard, 0, 0);
      split.Controls.Add(configCard, 1, 0);
      return split;
    }

    private Control CreateDetailsCard(string title, TextBox textBox) {
      var card = CreateCard();

      var layout = new BufferedTableLayoutPanel {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        BackColor = Color.White,
        Padding = new Padding(16)
      };
      layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
      layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

      var titleLabel = new Label {
        Dock = DockStyle.Fill,
        Text = title,
        Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
        ForeColor = Color.FromArgb(62, 50, 40),
        TextAlign = ContentAlignment.MiddleLeft
      };

      textBox.Dock = DockStyle.Fill;

      layout.Controls.Add(titleLabel, 0, 0);
      layout.Controls.Add(textBox, 0, 1);
      card.Controls.Add(layout);
      return card;
    }

    private static BufferedPanel CreateCard() {
      return new BufferedPanel {
        Dock = DockStyle.Fill,
        BackColor = Color.White
      };
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

      SetTextIfChanged(titleValueLabel, $"{totalPower:F1} W");
      SetTextIfChanged(subtitleValueLabel, $"{(snapshot.AcOnline ? "交流电" : "电池")} | CPU {snapshot.CpuPowerWatts:F1}W | GPU {(snapshot.MonitorGpu ? snapshot.GpuPowerWatts.ToString("F1") : "--")}W");

      SetTextIfChanged(cpuTempValueLabel, $"{snapshot.CpuTemperature:F1} °C");
      SetTextIfChanged(cpuPowerValueLabel, $"{snapshot.CpuPowerWatts:F1} W");
      SetTextIfChanged(gpuTempValueLabel, snapshot.MonitorGpu ? $"{snapshot.GpuTemperature:F1} °C" : "监控关闭");
      SetTextIfChanged(gpuPowerValueLabel, snapshot.MonitorGpu ? $"{snapshot.GpuPowerWatts:F1} W" : "--");
      SetTextIfChanged(batteryPowerValueLabel, batteryPower.HasValue ? $"{batteryPower.Value:F1} W" : "无功率读数");
      SetTextIfChanged(batteryDetailValueLabel, BuildBatteryDetail(snapshot, batteryPower));

      int batteryPercent = Math.Max(0, Math.Min(100, snapshot.BatteryPercent));
      if (batteryProgressBar.Value != batteryPercent)
        batteryProgressBar.Value = batteryPercent;

      SetTextIfChanged(muxValueLabel, FormatGraphicsMode(snapshot.GraphicsMode));
      SetTextIfChanged(adapterValueLabel, $"{FormatAdapterStatus(snapshot.SmartAdapterStatus)} / {(snapshot.AcOnline ? "AC" : "Battery")}");
      SetTextIfChanged(fanValueLabel, snapshot.MonitorFan ? $"{snapshot.FanSpeeds[0] * 100}/{snapshot.FanSpeeds[1] * 100} RPM" : "监控关闭");
      SetTextIfChanged(policyValueLabel, $"{snapshot.FanMode} | CPU {snapshot.CpuPowerSetting} | GPU {snapshot.GpuPowerSetting}");
      SetTextIfChanged(gpuCtlValueLabel, FormatGpuControl(snapshot.GpuStatus));
      SetTextIfChanged(capabilityValueLabel, BuildCapabilitySummary(snapshot));

      SetTextIfChanged(telemetryTextBox, BuildTelemetryText(snapshot, batteryPower));
      SetTextIfChanged(configTextBox, BuildConfigText(snapshot));
    }

    private static void SetTextIfChanged(Control control, string text) {
      if (control.Text != text)
        control.Text = text;
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

    protected override void WndProc(ref Message m) {
      if (m.Msg == WmEnterSizeMove) {
        suppressRefresh = true;
      } else if (m.Msg == WmExitSizeMove) {
        suppressRefresh = false;
        RefreshDashboard();
      }

      base.WndProc(ref m);
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
