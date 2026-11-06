# ATKLiteHub

轻量、极简且高效的 ATK 外设硬件控制与场景配置文件自动切换工具。基于 .NET 8 与 WPF 开发，无多余后台占用，即开即用。

---

## ✨ 核心特性

- **轻量低占用**：纯原生 WPF + .NET 8 构建，去除官方驱动复杂的内嵌网页与多余服务，内存占用低。
- **进程感知与配置自适应**：
  - 支持绑定游戏/应用程序进程。
  - 启动指定游戏时自动切换到对应外设 Profile。
  - 游戏退出后自动恢复默认日常配置。
- **悬浮动态提示**：切换 Profile 时在屏幕右上角显示无打扰动画 Toast 提示。
- **系统托盘沉浸**：支持最小化至系统托盘，后台静默守护，支持开机自启。
- **免安装开箱即用**：提供独立发布的无依赖绿色免安装包。

---

## 📁 目录结构

```text
ATKLiteHub/
├── publish/                   # 预编译绿色免安装包（解压/下载即用）
│   ├── ATKLiteHub.exe        # 启动程序
│   └── ...
├── App.xaml / App.xaml.cs    # 应用程序生命周期与单例控制
├── MainWindow.xaml (.cs)     # 主界面交互与控制面板
├── HardwareService.cs        # 硬件通信与底层指令控制
├── ProcessMonitorService.cs  # 进程前台监听与自动感知
├── ProfileToastWindow.xaml   # 切换配置浮动提示框
├── StartupHelper.cs          # 开机自启配置
├── TrayIconHelper.cs         # 系统托盘管理
└── ATKLiteHub.csproj         # 项目工程文件 (.NET 8.0-windows)
```

---

## 🚀 快速开始

### 方式 1：直接运行免安装包
进入项目根目录下的 [`publish/`](./publish/) 目录，直接双击运行 `ATKLiteHub.exe` 即可。

### 方式 2：通过源码编译

1. 确保安装了 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。
2. 克隆本仓库：
   ```bash
   git clone https://github.com/StarCenzZ/ATKLiteHub.git
   cd ATKLiteHub
   ```
3. 运行或构建项目：
   ```bash
   # 运行
   dotnet run

   # 发布绿色独立版
   dotnet publish -c Release -o ./publish
   ```

---

## 📄 开源许可

本项目基于 [MIT 许可证](./LICENSE) 开源。
