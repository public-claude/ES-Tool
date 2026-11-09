# ES Tool

[![源码检查与构建](https://github.com/ESCc13/ES-Tool/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/ESCc13/ES-Tool/actions/workflows/dotnet-desktop.yml)
[![License](https://img.shields.io/badge/license-ES%20Tool%20Public%20License-1370f3)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.8-512bd4)](https://dotnet.microsoft.com/)

**Minecraft 中国版第三方启动器** —— 基于原版挂服机（modpc）二次开发的图形界面版本插件，协议与功能保持一致。
界面直接使用 **PCL2（Plain Craft Launcher 2）程序集里的原生控件与完整前端资源**，不是仿制。

> 纯公益开源项目，**禁止倒卖**。项目地址：<https://github.com/ESCc13/ES-Tool>

![联机大厅](docs/screenshot-lobby.png)

## 这是什么

原版 `modpc` 是一套命令行工具（`Login.exe` + 一堆依赖 DLL），登录、启动、命令全靠敲。
ES Tool 把它整个搬到图形界面上：**业务逻辑一条没改**，仍然走原来的 `AppHost / Term / 命令链路`，
只是把输入输出换成了 PCL2 的原生 WPF 控件。

于是你得到的是：PCL2 的皮，modpc 的里子。

## 功能

- **PCL2 原生 UI（1:1）**
  窗口 850×500、无边框圆角、蓝色顶栏；控件全部来自 PCL2 程序集本体
  （`MyCard` / `MyButton` / `MyTextBox` / `MyComboBox` / `MyRadioButton` / `MyIconButton` / `MyListItem`），
  样式表原样取自 PCL2 的 `Application.xaml`，悬停变色、按下质感、下拉弹层等动画由 PCL2 内置动画线程执行。

- **登录 / 多账号**
  支持电脑版 `{json}`、手机版 `ey...` sauth、4399 `SDK4399...` 三类凭据；
  账号存在 `user/*.json`（明文登录态，转发前必须删）。

- **新账号昵称弹窗**
  服务端返回「还没有昵称」时弹窗接管原本的控制台 `ReadLine`：随机昵称 / 先跳过 / 确定，
  Enter 确定、Esc 跳过。窗口从后台线程 `Dispatcher.Invoke` 弹出，不会卡住 PCL 的动画线程。

- **联机大厅**
  全服在线地图池直接拉取（不再靠关键词猜房间），选中即进；房间 IP 一键查询，
  自动在官方大厅 / 自定义大厅两处检索，重名房间会列候选让你挑。

- **184 条命令全量入口**
  「功能」页把原版命令全部做成了可点条目，参数填完直接发；底部还有一条自由命令行。

- **单文件分发**
  `modpc\ES Tool.exe` 是自解压单文件：首次运行把依赖释放到 `ES_Tool_data\`，
  之后按 sha256 戳跳过重复释放，**永远不覆盖** `user\*`、`config.json`、`ui.json`。

## 截图

| 功能页（184 条命令） | 设置页（配置与诊断） |
|---|---|
| ![功能页](docs/screenshot-features.png) | ![设置页](docs/screenshot-settings.png) |

## 直接用（不编译）

到 [Releases](https://github.com/ESCc13/ES-Tool/releases/latest) 下载：

| 文件 | 说明 |
|---|---|
| `ES_Tool_v1.0.0_single.exe` | **单文件版，推荐**。丢进任意目录双击即可，首次运行自动把依赖释放到 `ES_Tool_data\`，之后按 sha256 戳跳过释放，永远不覆盖 `user\*`、`config.json`、`ui.json` |
| `ES_Tool_v1.0.0_full.zip` | 完整运行目录（`ModPcGUI.exe` + 依赖 DLL + `pcl\` + 源码），想看源码或自己改的拿这个 |

> 两个包都**不含账号数据** —— 出包前已清掉 `user\*.json`，设备指纹也换成了通用值。
> 运行目录里必须保留原版 modpc 的 `Login.exe` 及其依赖 —— ES Tool 是外壳，登录与协议实现仍在里面。

## 从源码构建

### 1. 环境

| 需要 | 版本 |
|---|---|
| .NET SDK | 8.0 或更高（只要能编 `net48` 就行） |
| 目标框架 | .NET Framework 4.8（Windows 10/11 自带） |
| 平台 | x86（`Prefer32Bit`，跟原版 modpc 一致） |

### 2. 准备运行环境（**仓库里没有这些，必须自己放**）

仓库只放 ES Tool 自己的源码。下面三类东西属于第三方 / 体积过大，按 `.gitignore` 排除了：

```
modpc/ModPc/                  ← 原版挂服机运行目录，整个放进来
    Login.exe                     （登录与协议的核心）
    Mcl.Core.dll  mcl.common.dll  Newtonsoft.Json.dll
    ocr.dll  OcrProxy.dll  WmCode.dll  lobbyip.exe
    msvcp*.dll  msvcr*.dll  vcruntime140.dll  ucrtbase.dll  concrt140.dll ...
    MPay/  pcl/  config.json  Keys.json  DummyUserList.json ...

ModPcGUI/pcl/                 ← PCL2 程序集，用来提供原生控件
    PCL.Core.dll                  （= 把 PCL2 的 "Plain Craft Launcher 2.exe" 改名而来，
                                    内部程序集标识不变，控件与资源照旧可用）
    MeloongCore.dll  MeloongCore.Wpf.dll
    NAudio.dll  Ookii.Dialogs.Wpf.dll  CacheCow.*.dll  Imazen.WebP.dll  libwebp64.dll ...

ModPcGUI/src/PclFont.ttf      ← PCL2 内嵌的英文字体「PCL English」，只有标题 Logo 用
```

几个坑先说明：

- **`PCL.Core.dll` 必须叫这个名字**。程序集查找是按文件名来的，`Boot.cs` 里有一段
  `AssemblyResolve` 把 `Plain Craft Launcher 2` 映射到 `PCL.Core.dll`；
  少这一步会静默回落到经典界面。
- **`PclFont.ttf` 是可选**（`csproj` 里写了 `Condition="Exists(...)"`）。没有它照样能编译运行，
  只是标题 Logo 退回微软雅黑。

### 3. 编译

```bat
dotnet build ModPcGUI\src\ModPcGUI.csproj -c Release
```

产物：`ModPcGUI\src\bin\Release\net48\ModPcGUI.exe`，把整个 `net48\` 目录拷进 `modpc\ModPc\` 就能跑。

### 4. 打包成单文件（可选）

```
_tools\make_single_exe.py    # 把 modpc\ModPc 压成 payload.zip 内嵌进启动器壳
```

打包脚本会先做一次**发布前消毒**：删掉 `user\*.json`（明文账号 cookie）、测试日志、
`config.json.bak`，并把 `sa_data*.json` 里的真实 MAC / UDID / 显卡型号换成通用值。
`sa_data*.json` 不能直接删 —— `app.ReadJson()` 读不到会返回 null，登录请求体会缺字段直接失败。

## 目录结构

```
ModPcGUI/
  src/
    UI/                     PCL2 原生 UI 前端（PclMainWindow / 各页布局 / 弹窗）
      PclMainWindow.cs      窗口与业务桥接
      PclMainWindow.Layout.cs  五个页面 + PCL 样式加载
      PclMainWindow.Lobby.cs   联机大厅
      SetNameDialog.cs     新账号昵称弹窗（原生 WPF，不用 PCL 控件）
      Boot.cs              启动、程序集解析、异常兜底
      AppHost.cs           登录/启动流程，挂 NamePrompt 钩子
    text/                   原版命令实现（app / Function / Http ...）
    Login.*/                登录、协议、鉴权（原版）
    ConsoleAppLogin.NetEase 控制台外壳与 HTTP
    WPFLauncher.*/          网易启动器协议（原版）
    ModPcGUI.csproj
  使用说明.md
免责声明.txt
LICENSE
```

## 开发者

- **kaka**（[ESCc13](https://github.com/ESCc13)）
- **Publish VarInt** （[Github](https://github.com/public-claude)）
- **BivUraMLP**

## 声明

1. 本项目为非官方个人研究项目，与网易 / Mojang / 微软**无任何关系**。
2. **纯公益开源，禁止倒卖**：不得收费分发、卡密售卖、捆绑销售，详见 [LICENSE](LICENSE)。
3. `user/` 目录是**明文登录态**，转发本软件前必须先删除，否则等于把账号送出去。
4. 使用即自担风险：封号、删档、IP 暴露、以及你对他人服务器所做的一切，均与作者无关；
   锁服 / 崩服 / 刷屏 / 假人填充等破坏性功能**仅限自建服务器测试**。
5. 界面控件与样式来自 PCL2（Plain Craft Launcher 2），版权归其作者所有，本项目仅做调用。
   原版挂服机（modpc）同理。

完整条款见 [免责声明.txt](免责声明.txt)。
