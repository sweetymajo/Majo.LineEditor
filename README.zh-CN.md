<div align="center">

# Majo.LineEditor

**一个小巧、跨平台的 .NET 交互式行编辑器。**

Windows 使用原生控制台能力，Linux 使用基于 linenoise 的 POSIX 后端，并通过尽可能精简的托管 API 为命令行程序提供稳定的交互式输入体验。

[![NuGet](https://img.shields.io/nuget/v/Majo.LineEditor?style=flat-square&logo=nuget&logoColor=white)](https://www.nuget.org/packages/Majo.LineEditor)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
![Windows](https://img.shields.io/badge/Windows-supported-0078D4?style=flat-square&logo=windows&logoColor=white)
![Linux x64](https://img.shields.io/badge/Linux-x64-FCC624?style=flat-square&logo=linux&logoColor=black)
![C11](https://img.shields.io/badge/native-C11-A8B9CC?style=flat-square&logo=c&logoColor=black)

[English](./README.md) · **简体中文**

</div>

---

## 项目简介

`Majo.LineEditor` 为 .NET 控制台程序提供交互式行编辑能力，同时避免让业务程序直接依赖某一套平台专用终端实现。

公共 API 保持简单，内部根据运行平台选择对应后端：

- **Windows** 使用托管实现的 Win32 控制台后端；
- **Linux / POSIX** 使用对 vendored `linenoise` 进行封装的原生 C 后端；
- Linux 原生库以 runtime asset 的形式直接随仓库提供，因此普通 `.NET` 编译和发布过程**不需要** GCC 或 CMake。

因此，你可以直接在 Windows 上发布 `linux-x64` 版本，再把发布结果放到 Linux 上运行，而不必为了 `Majo.LineEditor` 额外准备一次 Linux 原生编译环境。

## 主要特性

- 单行编辑与横向视口
- 自动折行的多行编辑
- 可配置容量的命令历史
- Unicode 感知的光标移动和显示宽度处理
- Home / End / Left / Right 导航
- Backspace 与 Delete 编辑
- Up / Down 历史记录浏览
- `Ctrl+C` 作为中断结果返回
- `Ctrl+D` 的 EOF / 向前删除语义
- 终端窗口大小变化处理
- 支持取消的异步读取
- `WriteAbove(...)`：后台输出时保留当前正在编辑的输入行
- Windows 与 POSIX 分别进行平台针对性的渲染优化
- 预编译的 `linux-x64` native runtime
- 普通 `.NET build` / `publish` 不调用 GCC 或 CMake

## 安装

`Majo.LineEditor` 已发布至 [NuGet.org](https://www.nuget.org/packages/Majo.LineEditor)：

```bash
dotnet add package Majo.LineEditor
```

## 快速开始

创建一个 `LineEditor` 实例：

```csharp
using Majo.LineEditor;

using var editor = new LineEditor(new LineEditorOption
{
    Prompt = "> ",
    CommandBufferSize = 4096,
    HistoryCount = 100,
    PollInterval = 20,
    MultiLine = false
});

ReadResult result = await editor.ReadLineAsync();
```

`ReadLineAsync()` 返回 `ReadResult`，通过读取状态区分正常提交、中断以及输入结束。

如果程序需要在用户输入过程中输出后台消息，可以使用：

```csharp
editor.WriteAbove("[background] Connection established.");
```

`WriteAbove(...)` 会根据当前平台保存并恢复正在编辑的内容，而不是让普通控制台输出破坏输入行。

## 配置项

`LineEditorOption` 用于控制编辑器行为：

| 选项 | 说明 |
| --- | --- |
| `Prompt` | 显示在可编辑输入之前的提示符。 |
| `CommandBufferSize` | 命令允许占用的最大 **UTF-8 字节数**。 |
| `HistoryCount` | 在内存中保留的非空已提交命令数量；`0` 表示禁用历史记录。 |
| `PollInterval` | 等待控制台输入，以及处理取消/窗口变化时使用的轮询间隔。 |
| `MultiLine` | `false` 使用单行横向视口；`true` 启用自动折行的多行编辑。 |

历史记录属于当前 `LineEditor` 实例，并在该实例生命周期内保存在内存中。

## 读取语义

编辑器主要返回三种读取状态：

| 状态 | 含义 |
| --- | --- |
| `Accepted` | 用户按下 Enter，提交当前输入。 |
| `Interrupted` | 用户按下 `Ctrl+C`。 |
| `EndOfInput` | 用户请求结束输入，例如在空行时按下 `Ctrl+D`。 |

当输入内容非空时，`Ctrl+D` 不会立即结束输入，而是执行向前删除。

同一个编辑器同一时间只允许存在一个活动的读取操作。

## `WriteAbove`

交互式程序经常需要在用户仍然输入命令时显示异步状态，例如连接事件、日志或后台任务结果。此时直接调用普通的 `Console.WriteLine` 很容易破坏提示符、光标位置，或者留下旧输入残影。

`WriteAbove(string content)` 就是为这种场景设计的：

```text
[background] peer connected
> still typing here...
```

内部实现因平台而异：

- **Windows** 会保护当前输入区域，并在可用时使用同步终端输出，尽量避免中间渲染帧造成的闪烁；
- **POSIX** 会临时隐藏并恢复 linenoise 的编辑状态，同时保持逻辑输入行不变。

如果传入的 `content` 本身没有换行结尾，`WriteAbove(...)` 会自动补充换行。

## 平台架构

```text
Majo.LineEditor
│
├─ LineEditor
│  └─ 精简的跨平台公共 API
│
├─ Backends
│  ├─ Win32
│  │  ├─ 托管行编辑器
│  │  └─ Win32 Console API 互操作
│  │
│  └─ Posix
│     ├─ 托管后端
│     └─ Native
│        ├─ majo_line_editor.c
│        ├─ linenoise.c
│        ├─ linenoise.h
│        └─ CMakeLists.txt
│
└─ runtimes
   └─ linux-x64
      └─ native
         └─ libmajo_line_editor.so
```

### Windows 后端

Windows 后端直接使用 Win32 Console API，负责输入编辑、历史记录导航、光标移动、渲染、显示宽度计算、窗口大小变化恢复以及后台输出。

Windows 托管渲染器使用 `Wcwidth` 包进行显示宽度处理。

### POSIX 后端

POSIX 后端使用一个很薄的 native wrapper 封装 vendored `linenoise`。

wrapper 只暴露托管层实际需要的操作，例如启动/停止编辑、轮询输入、向 linenoise 提交输入、准备历史记录、同步窗口尺寸以及 `WriteAbove`。

当前 vendored linenoise 的上游版本和有意保留的本地修改记录在：

```text
Backends/Posix/Native/LINENOISE_LOCAL_CHANGES.md
```

对 linenoise 的直接修改应尽可能少，并且必须保持可追踪。

## Native 库的构建方式

POSIX native 库被有意设计为**不参与普通 `.NET` 编译流程**。

仓库同时保存：

```text
Backends/Posix/Native/
    native 源码与 CMake 工程

runtimes/linux-x64/native/
    .NET 构建实际使用的预编译 libmajo_line_editor.so
```

普通：

```bash
dotnet build
```

或者直接在 Windows 上执行：

```bash
dotnet publish -r linux-x64
```

都不会调用 GCC、CMake、WSL 或 Linux 虚拟机。构建过程只会把仓库中已经准备好的 native library 带入最终输出。

只有 native 源码发生变化时才需要重新编译 `.so`。完整的开发环境要求和 native library 构建方式请参阅 [CONTRIBUTING.md](./CONTRIBUTING.md)。

> `Wcwidth` 属于 Windows 托管后端；POSIX native library 的编译和链接不包含它。

## Runtime Asset 策略

`libmajo_line_editor.so` 是需要提交到仓库的运行时依赖，而不是一个可以随时丢弃的普通构建中间产物。

这是有意设计的：

```text
Native 源码
    ↓ 仅在源码变化时显式使用 GCC/CMake 编译
libmajo_line_editor.so
    ↓ 提交到 runtimes/linux-x64/native
普通 .NET build / publish
    ↓
最终应用输出
```

这样可以让日常 .NET 开发继续保持跨平台和轻量，同时又允许 POSIX 后端在真正有价值的地方使用 native C 实现。

## 交互式控制台要求

`Majo.LineEditor` 面向真实的交互式 Console / TTY。

重定向后的标准输入或标准输出不属于受支持的编辑环境。编辑器同时限制单进程只存在一个活动的行编辑器实例，避免多个后端争用同一套终端状态。

## 参与开发

欢迎参与 `Majo.LineEditor` 的开发。

开发环境、交互式测试、本地 NuGet 包验证、POSIX native 开发以及 vendored linenoise 的维护规则，请参阅 [CONTRIBUTING.md](./CONTRIBUTING.md)。

`CONTRIBUTING.md` 目前使用英文编写。

## 设计原则

项目刻意保持较小的职责范围：

- 公共 API 保持平台无关；
- 平台差异留在各自 backend 内部；
- native API 确实能明显改善终端行为时才使用 native 实现；
- 不把 native toolchain 强行塞进日常 managed build；
- vendored 第三方源码的本地修改保持最小、清晰、可审计；
- 优先解决真实存在的终端行为，再在实际需要时增加抽象。

---

<div align="center">

作为 Majo 系列项目中一个专注的行编辑组件。

[English](./README.md)

</div>
