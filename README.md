# VisionMaven

生产线机器视觉与深度学习模型部署框架。单机单进程桌面应用，同时承担**工程配置态**（工程师）与**运行监控态**（操作员）两种职责。

完整设计说明见 `docs/开发文档.md`。**界面负责操作，文档负责解释**——界面内不放置任何说明性文字。

---

## 1. 解决方案结构

```
VisionMaven.sln
├─ Directory.Build.props           统一 TargetFramework / LangVersion / Nullable / 警告即错误 / 输出目录
├─ Directory.Packages.props        集中管理 NuGet 版本
├─ clean-rebuild.ps1               一键清理并重新编译
├─ bin/                            构建产物（与 src 同级，由 Directory.Build.props 重定向）
│  └─ {ProjectName}/{Configuration}/{TargetFramework}/
├─ obj/                            中间产物（同上；含 NuGet 资产文件与 WPF 临时工程）
├─ src/
│  ├─ VisionMaven.App              启动壳 + Shell + 登录 + 组合根（唯一可执行工程）
│  ├─ VisionMaven.Core             零依赖契约层：设备/算子/推理/流程契约、领域模型、事件、Shell 契约
│  ├─ VisionMaven.Infrastructure   EF Core SQLite、工程 JSON 读写、权限审计、日志
│  ├─ VisionMaven.Vision           OpenCvSharp 算子、标定、ONNX Runtime 推理、结果叠加
│  ├─ VisionMaven.Flow             流程引擎、节点库、设备会话、多工位运行时
│  ├─ VisionMaven.Devices.Mock     模拟相机 / PLC / 光源 / MES
│  ├─ VisionMaven.Devices.Hikvision 海康威视 MVS（P/Invoke）
│  ├─ VisionMaven.Devices.GenTL    GenTL Producer 探测
│  ├─ VisionMaven.Devices.Light.Serial / Light.Tcp  串口 / TCP 光源控制器
│  ├─ VisionMaven.Devices.Modbus / S7              Modbus TCP / 西门子 S7
│  ├─ VisionMaven.Devices.Mes                      MES HTTP 客户端
│  ├─ VisionMaven.Modules.Shared   模块公共基类、Schema 驱动参数表单、图标字形、图像互操作
│  └─ VisionMaven.Modules.*        首页 / 视觉流程 / 算法 / 模型 / 参数设置 / 设备 / 通讯 / 用户 / 项目管理
└─ tests/                          见第 6 节「已知缺口」
```

### 与文档的两处结构性调整（有意为之）

| 调整 | 原因 |
|---|---|
| 新增 `VisionMaven.Modules.Shared` | 模块之间不得相互引用、也不得引用 `App`；页面基类、参数表单控件、图标映射需要一处共用实现 |
| Shell 契约（`IActionBarService`、`INavigationCatalog`、`ITopMenuRegistry`、`IDeviceTypeRegistry`、`IDialogService`、`IProjectSession`）下沉到 `VisionMaven.Core.Shell` | 同上。`System.Windows.Input.ICommand` 属于 BCL，`Core` 仍保持零外部依赖 |

依赖方向严格单向向下：`App → Modules.* → Flow → Vision → Core`，`Devices.* → Core`。

---

## 2. 运行环境

| 项 | 要求 |
|---|---|
| 操作系统 | Windows 10 x64 1809+ / Windows 11 |
| .NET SDK | .NET 8 SDK（本仓库使用 .NET 9 SDK 构建 `net8.0-windows` 目标，已验证通过） |
| 相机 SDK（可选） | 海康 MVS：安装后复制 `MvCameraControl.dll`、`MvCameraControl.Net.dll` 到输出目录 |
| GPU 推理（可选） | NVIDIA 驱动 + CUDA；以 `-p:VisionMavenGpu=true` 构建切换到 `Microsoft.ML.OnnxRuntime.Gpu` |
| ONNX Runtime / OpenCvSharp | 由 NuGet 引入，随发布输出 |

**无硬件也可完整运行**：默认工程使用 `mock.camera` / `mock.plc` / `mock.light` / `mock.mes`，可直接跑通「采集 → 预处理 → 分割 → Blob → 判定 → 落盘」全链路。

---

## 3. 构建与运行

### 一键清理并重新编译

```powershell
.\clean-rebuild.ps1                     # 清理 bin/obj → restore → Debug 编译
.\clean-rebuild.ps1 -Configuration Release
.\clean-rebuild.ps1 -FastRebuild        # 只删 bin、跳过 restore（仅改代码时最快）
.\clean-rebuild.ps1 -CleanOnly          # 只清理
.\clean-rebuild.ps1 -Run                # 编译后直接启动
```

脚本会先结束占用输出目录的 `VisionMaven` 进程，再逐工程删除输出目录，最后编译；任一步失败以非 0 退出码结束。

### 手动构建

```powershell
dotnet build VisionMaven.sln -c Debug
dotnet run --project src/VisionMaven.App/VisionMaven.App.csproj
```

### 构建产物位置（不污染源码树）

`bin` 与 `obj` 被重定向到**仓库根**，与 `src` 同级，任何工程目录内都不会生成构建产物：

```
bin/{ProjectName}/{Configuration}/{TargetFramework}/
obj/{ProjectName}/{Configuration}/{TargetFramework}/
```

该行为由 `Directory.Build.props` 的 `BaseOutputPath` / `BaseIntermediateOutputPath` 控制。
两者必须同时设置：`obj` 存放 `project.assets.json`、中间程序集与 WPF 临时工程，体量远大于 `bin`，只改 `bin` 达不到隔离效果。

> 注：运行时数据目录（`projects/`、`data/`、`logs/`、`plugins/`）创建在**可执行文件所在目录**旁，即 `bin/VisionMaven.App/.../` 下，不会写入仓库根。

### 首次运行

1. 首次启动自动创建 `data/visionmaven.db`（SQLite，WAL 模式）与 `projects/`、`logs/`、`plugins/` 目录。
2. 内置管理员：账号 `admin`，初始密码 `Admin@123`，**首次登录强制修改密码**。
3. 「项目 → 新建工程」生成最小可用工程（1 工位 + 1 相机 + 1 流程），随后可「项目 → 保存」并「Home → 启动」。
4. 无相机时首页实时画面为空属正常；`mock.camera` 默认可直接出图。

### 默认角色

| 角色 | 编码 | 权限 |
|---|---|---|
| 管理员 | `Admin` | 全部权限（含用户与审计） |
| 工程师 | `Engineer` | 除用户管理与审计外的全部权限 |
| 操作员 | `Operator` | 仅首页 + 运行控制 + 报警确认 |

---

## 4. 目录布局（运行时）

```
VisionMaven.exe
├─ projects/{ProjectId}/   project.json / models / labels / templates / calibration / images
├─ data/                   visionmaven.db、backup/（迁移前自动备份，保留 10 份）
├─ logs/                   visionmaven-YYYYMMDD.log（按天轮转，保留 30 天，单文件 50MB）
├─ plugins/                驱动插件目录（扫描带有 [VisionDriver] 的 DLL）
└─ models/                 全局模型缓存
```

---

## 5. 发布

```powershell
dotnet publish src/VisionMaven.App/VisionMaven.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o publish/
```

**不要**启用 `PublishSingleFile` / `PublishTrimmed`：ONNX Runtime 与相机 SDK 的 native DLL 必须保留在目录中，反射与序列化也会被裁剪破坏。

启用 CUDA / TensorRT 执行提供程序：

```powershell
dotnet publish src/VisionMaven.App/VisionMaven.App.csproj -c Release -p:VisionMavenGpu=true -o publish-gpu/
```

GPU 包不可用时，推理节点会在加载阶段自动降级到 CPU 并写入告警日志，不中断运行。

---

## 6. 已知缺口（如实记录）

| 项 | 状态 |
|---|---|
| `VisionMaven.Tests`（xUnit 单元 / 集成测试） | **未实现**。当前验证手段为整体构建通过 + Mock 驱动全链路手工验证 |
| GenTL 相机取流 | **未实现**。仅做 `.cti` Producer 探测与诊断上报，驱动恒为不可用并给出明确原因；取流需厂商原生驱动 |
| 海康 MVS 驱动 | 已实现 P/Invoke 取流与常用参数读写，但**未在真机验证**；结构体布局以 `MvCameraControl.h` 为准，接入前需按现场 MVS 版本核对 |
| 界面图标 | 文档要求使用 HandyControl `PackIcon`，但 HandyControl 3.5.1 未通过任何 `XmlnsDefinition` 暴露该类型（已实测 `handymodel.github.io` 与 `clr-namespace` 两种写法均失败），改用 `Segoe MDL2 Assets` 字形映射（`IconGlyphs`）实现同一语义 |
| HandyControl 主题 | 未合并 HandyControl 皮肤字典（会覆盖自定义设计令牌），全部样式由 `App/Themes/*.xaml` 提供 |
| 设备页「新增设备」 | 支持按设备类型新增并落盘到工程配置；驱动选择取该类型下第一个可用驱动，未提供驱动下拉选择 |
| 用户页「新增用户」 | 未提供新增表单（仅提示可用角色编码）；编辑、禁用、删除、重置密码、权限矩阵、审计查询均可用 |
| EF Core Migrations | 采用内置有序升级脚本 + 迁移前自动备份 + `Database.SchemaVersion` 版本记录，未使用 `dotnet-ef` 生成迁移文件 |
| 数据库表 `RolePermissions` | 以独立实体体现（`RoleDefinition.Permissions` 通过 Fluent API `Ignore`），符合文档表设计 |

---

## 7. 扩展点速查

| 需求 | 做法 |
|---|---|
| 新增相机 / PLC / 光源 / MES 驱动 | 新建工程 → 实现 `IDeviceDriverProvider` + 对应设备契约 → 标注 `[VisionDriver("key", DeviceKind.X)]` → 注册提供程序（或放入 `plugins/` 且提供无参构造） |
| 新增流程节点 | 新建实现 `IFlowNode` 的类 → 标注 `[FlowNode("typeKey", "显示名", Order = n, Group = "分组")]` → 声明 `ParameterSchema`；界面自动出现该节点并生成参数表单 |
| 新增图像算子 | 继承 `OperatorBase` → 实现 `TypeKey` / `Schema` / `Execute` → 在 `VisionServiceCollectionExtensions.RegisterOperators` 登记 |
| 新增设备子页签类型 | 在 `DeviceKind` 追加枚举值 → 注册 `DeviceTypeDescriptor`；设备页零改动 |
| 新增导航页 | 新建 `Modules.{Name}` → `IModule.OnInitialized` 中 `INavigationCatalog.Register(...)` → App 的 `ConfigureModuleCatalog` 登记模块 |
| 新增一类产线 | 全程配置：新建工程 → 首页绑定工位与设备 → 流程页编排 → 算法/模型页调参 → 参数页试跑 → 保存 / 导出 `.vmproj` |

---

## 8. 关键约束（编码时请遵守）

- 目标框架 `net8.0-windows`，`Nullable` / `ImplicitUsings` 开启，**警告视为错误**。
- 异步全程 `async`/`await`，禁止 `.Result` / `.Wait()`；库代码使用 `ConfigureAwait(false)`。
- `Mat` 与 `InferenceSession` 必须释放；流程执行期创建的帧由 `FlowContext.Track` 登记并在执行结束时统一释放。
- 界面红线：只放字段名、按钮、校验错误与必要取值提示；不放灰色说明小字、提示段落、题头描述、装饰元素、窗体内部 LOGO。
- 日志禁止写入图像像素、MES 完整报文、密码密钥明文与高频连续帧。
