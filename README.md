# VisionMaven

生产线机器视觉与深度学习模型部署框架。

## 定位

运行于单台工控机、单进程的桌面应用，用于在产线现场部署视觉检测与深度学习模型推理。

框架层与产线无关，产线差异全部收敛到工程配置与插件驱动中：

- 单相机 / 多相机
- 单工位 / 多工位
- 多设备（相机、光源控制器、PLC、机器人、MES）

新增一条产线只需配置，不需要改代码。

## 文档

完整开发文档见 [docs/开发文档.md](docs/开发文档.md)，包含：

- 技术栈与选型理由
- 解决方案结构与依赖方向
- 架构设计与运行时数据流
- 界面规范（三行式布局、Region 划分、设计令牌、UI 红线）
- 核心契约定义（设备驱动、算子、推理引擎、流程节点、Shell 服务）
- 配置 Schema（`project.json` 完整示例与字段说明）
- 数据库设计
- 检测流程引擎、视觉算法与标定、推理部署
- 扩展指南（新增驱动、节点、模块、设备类型、产线）
- 权限与用户管理、日志规范、测试策略、部署与发布

## 技术栈

| 领域 | 选型 |
|---|---|
| 运行时 | .NET 8（`net8.0-windows`），C# 12 |
| UI | WPF + Prism 9（Prism.DryIoc）+ HandyControl 3.5.x |
| 图像处理 | OpenCvSharp4 |
| 模型推理 | Microsoft.ML.OnnxRuntime（CPU / CUDA / TensorRT EP） |
| 持久化 | EF Core 8 + Microsoft.Data.Sqlite |
| 日志 | Serilog |
| PLC / 机器人 | HslCommunication（Modbus TCP、西门子 S7） |
| 光源控制器 | System.IO.Ports（RS232/RS485）+ TcpClient |
| MES | HttpClient + Polly |
| 相机 SDK | 海康 MVS、GenICam/GenTL |
| 测试 | xUnit + Moq |

## 运行环境要求

| 项 | 最低 | 推荐 |
|---|---|---|
| 操作系统 | Windows 10 x64 1809+ | Windows 10/11 x64 22H2 或 Windows Server 2019 |
| CPU | 4 核 | 8 核以上 |
| 内存 | 8 GB | 16 GB 以上 |
| 显卡 | 集成显卡（CPU 推理） | NVIDIA GTX 1660 以上（CUDA 推理） |
| 磁盘 | 系统盘 20 GB | 数据盘 500 GB 以上 |
| 屏幕 | 1366×768 | 1920×1080 |

## 构建

需安装 .NET 8 SDK。

```powershell
dotnet restore
dotnet build -c Debug
dotnet run --project src/VisionMaven.App
```

### 第三方 SDK（可选，缺失不影响启动）

| SDK | 用途 | 获取方式 |
|---|---|---|
| 海康 MVS | 海康相机取流 | 安装 MVS 客户端，复制 `MvCameraControl.dll` 与 `MvCameraControl.Net.dll` 到输出目录 |
| GenTL Producer | Basler / 大恒 / 迈德威视等 | 安装各厂商 Runtime，取其 `.cti` 文件 |

程序启动时逐个探测 SDK，缺失只记 `Warning` 并不阻断启动。

## 发布

```powershell
dotnet publish src/VisionMaven.App/VisionMaven.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o publish/
```

`PublishSingleFile` 必须为 `false`：ONNX Runtime 与相机 SDK 的 native DLL 需要保留在目录中，单文件打包会导致 native 加载失败。

## 目录结构

```
VisionMaven.sln
├─ Directory.Build.props          统一编译属性
├─ Directory.Packages.props       集中管理 NuGet 版本
├─ docs/开发文档.md                开发文档
├─ src/
│  ├─ VisionMaven.App             启动壳 + Shell + 登录 + 托盘
│  ├─ VisionMaven.Core            零依赖契约层
│  ├─ VisionMaven.Infrastructure  持久化 / 配置 / 权限审计 / 文件存储
│  ├─ VisionMaven.Vision          算子 + 标定 + ONNX 推理
│  ├─ VisionMaven.Flow            流程引擎 + 多工位运行时
│  ├─ VisionMaven.Devices.*       设备驱动（海康 / GenTL / 光源 / Modbus / S7 / MES / Mock）
│  └─ VisionMaven.Modules.*       Prism 业务模块（8 个导航页 + 项目管理）
└─ tests/VisionMaven.Tests        单元与集成测试
```

## 运行时目录

```
projects/{ProjectId}/project.json   工程配置
projects/{ProjectId}/models/        ONNX 模型
projects/{ProjectId}/labels/        标签集
projects/{ProjectId}/templates/     模板
projects/{ProjectId}/calibration/   标定文件
projects/{ProjectId}/images/        结果图片
data/visionmaven.db                 SQLite 数据库
logs/                               Serilog 轮转日志
plugins/                            驱动插件
```

## 开发状态

| 里程碑 | 内容 | 状态 |
|---|---|---|
| M0 | 开发文档 | 已完成 |
| M1 | 解决方案骨架与 Prism 启动壳、三行式 Shell、导航、操作按钮栏、日志栏、登录 | 待开始 |
| M2 | Shell 服务层（操作按钮栏服务、导航目录、菜单注册表、设备类型注册表、日志 UI Sink） | 待开始 |
| M3 | Core 契约与领域模型、SQLite 持久化、工程 JSON 读写、权限与审计 | 待开始 |
| M4 | 驱动框架与首批驱动 | 待开始 |
| M5 | OpenCvSharp 算子库与标定 | 待开始 |
| M6 | ONNX Runtime 推理引擎 | 待开始 |
| M7 | 流程引擎与多工位运行时 | 待开始 |
| M8 | 8 个导航页界面 | 待开始 |
| M9 | 集成验证与测试 | 待开始 |
