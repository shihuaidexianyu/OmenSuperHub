# OmenSuperHub 传感器来源表

本文档只描述当前项目已经收敛后的目标硬件：

- `Intel Core i9-13900HX`
- `NVIDIA GeForce RTX 4060 Laptop GPU`

## 当前项目实际使用的读数

| 读数 | 代码入口 | 实际来源 | 是否依赖 PawnIO | 说明 |
|---|---|---|---|---|
| `CpuTemperature` | `src/App/Services/HardwareTelemetryService.cs` | `LibreHardwareMonitor` CPU 传感器 | 是 | 当前固定读取 `CPU Package` |
| `CpuPowerWatts` | `src/App/Services/HardwareTelemetryService.cs` | `LibreHardwareMonitor` CPU 功耗传感器 | 是 | 当前固定读取 `CPU Package` |
| `GpuTemperature` | `src/App/Services/HardwareTelemetryService.cs` | `LibreHardwareMonitor` NVIDIA GPU 温度传感器 | 否 | 优先 `GPU Hot Spot`，其次 `GPU Core` |
| `GpuPowerWatts` | `src/App/Services/HardwareTelemetryService.cs` | `LibreHardwareMonitor` NVIDIA GPU 功耗传感器 | 否 | 优先 `GPU Package`，其次 `GPU Power` |
| `TemperatureSensors` | `src/App/Services/HardwareTelemetryService.cs` | CPU + NVIDIA GPU 温度池 | 混合 | 仅用于控制和 UI 快照 |
| `FanSpeeds` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `hpqBIntM` `0x2D` | 否 | 两个风扇当前转速原始级别 |
| `GraphicsMode` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x52` | 否 | Hybrid / Discrete / Optimus |
| `GpuStatus` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x21` | 否 | `CustomTgp`、`Ppab`、`DState` 等 |
| `SystemDesignData` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x28` | 否 | 风扇控制、极限模式等能力位 |
| `FanTypeInfo` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x2C` | 否 | 风扇类型 |
| `SmartAdapterStatus` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x0F` | 否 | 适配器能力状态 |
| `KeyboardType` | `src/Hardware/OmenHardware.cs` | HP BIOS WMI `0x2B` | 否 | 键盘类型 |
| `BatteryTelemetry` | `src/App/Services/HardwareTelemetryService.cs` | WMI `BatteryStatus` | 否 | 充放电、电压、容量 |
| `BatteryPercent` | `src/App/Program.cs` | `SystemInformation.PowerStatus` | 否 | Windows 电源状态 |

## 当前项目不会再处理的路径

- AMD CPU 传感器兼容逻辑
- AMD dGPU 传感器兼容逻辑
- Intel iGPU 功耗单独兼容逻辑
- 主板 Super I/O、内存 SPD、DIMM 温度之类未启用硬件组

## 结论

- `PawnIO` 现在主要影响 `Intel CPU 温度` 和 `Intel CPU 功耗`
- `RTX 4060 Laptop` 的 `GPU 温度 / GPU 功耗` 主要走 `NVAPI` / `NVML`
- OMEN 专有控制能力主要走 HP BIOS WMI，不走 `PawnIO`
