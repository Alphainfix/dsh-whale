# DSH 小鲸鱼 · Windows 版

Windows 系统托盘上的小鲸鱼，由 [genius-FXC](https://github.com/genius-FXC) 从 macOS 版移植。
macOS 版的说明见 [`../mac/README.md`](../mac/README.md)。

## 使用

下载 [Releases](https://github.com/Alphainfix/dsh-whale/releases) 里的 `DSHWhale-<版本>-windows-x64.zip`，解压后运行 `DSHWhale.exe`，
旁边的 `whale.png` 和 `whale-windows.ico` 要一起留着。从源码构建的话，程序在 `build\DSHWhale.exe`。

第一次运行时 Windows 可能弹出「Windows 已保护你的电脑」：这个程序没有代码签名，点「更多信息」→「仍要运行」即可。

小鲸鱼常驻系统托盘：点击鲸鱼显示面板，右键可以打开设置或退出。面板失去焦点会自动收起，进程还在。

- 双击 `DSHWhale.exe` 显示面板；托盘里已经有小鲸鱼时，会把它的面板叫出来。
- `--open`：先启动没在运行的服务，再打开 DSH 网页。
- `--settings`：打开设置。
- `--autostart`：只待在托盘里，开机自启用的就是它。
- `--demo`：用示例数据，不连接、不启动也不停止真实的 DSH。

「启动并打开 DSH」会先启动没在运行的服务，再打开网页；服务运行中则显示「打开 DSH 界面」。

不需要重新安装现有的 DSH。小鲸鱼会自动查找 PATH、npm 全局安装和 npx 缓存，读取
`@deepseek-ai/dsh/package.json` 里 `bin.dsh` 指向的入口，用 Node 运行它。Node 路径、DSH 入口和工作目录都可以在
「Windows 连接与启动选项」里指定；工作目录默认是用户主目录，和 macOS 版一样。
npx 缓存被清掉以后，要先重新运行一次 npx 把 DSH 装回来；小鲸鱼不会自己安装或升级 DSH。

## 支持哪种 DSH

小鲸鱼托管的是用 npm 安装的 `dsh web`（全局安装，或 npx 缓存里的那份），按 3080 端口认服务。

DSH 桌面版自己管理服务：它的后台在 127.0.0.1 上开本机网页服务，窗口经它工作，但端口不是 3080（0.2.1 起每次启动随机，
0.1.7–0.2.0 是 19387）；npm 装的 `dsh` 不能启动桌面版的 `desktop` 配置，也不能给它装插件（桌面版自带的 `dsh` 命令可以装插件）。
小鲸鱼只看 3080，所以看不到桌面版的状态和余额。「Windows 连接与启动选项」里有一项「DSH 怎么运行」：

- **自动**（默认）：托管 `dsh web`；但如果 `DSH_HOME`（默认 `%USERPROFILE%\.dsh`）的 `profiles` 里只有桌面版的 `desktop`、
  没有 `web`，就不另起服务，免得两套 DSH 共用同一个 `.dsh` 文件夹：两边会各自加载同一份会话和提醒表，提醒可能两边各响一次，
  提醒表也会互相覆盖。两种配置都有时，自动模式照样会在桌面版旁边启动 `dsh web`；这种情况请选下面「只看状态」。
- **由小鲸鱼启动和看护 dsh web**：总是托管。
- **只看状态，不启动也不停止**：3080 上有 DSH 在运行就显示状态和余额（桌面版不在 3080，看不到），开关和重启按钮变灰。

同一时间只开一个托管程序（比如别再让「任务计划程序」另外去拉起 `dsh web`）：两边管的是同一个 3080 端口，
一边停了服务，另一边的看门机制可能又把它启动起来。

## 已移植的行为

- 服务开启、关闭、重启；识别 3080 端口上已有的 DSH；HTTP 401 也算服务存活。
- 20 秒检查一次；手动停止后不再自动启动；连续三次短暂启动失败后停止重试。停止或重启失败时，看门照常继续。
- 服务操作串行化，启动中的进程不会因为端口还没就绪被重复启动。
- 先对能核实身份、并且用独立控制台的 DSH 进程发送 Ctrl+C，让 Node/DSH 正常清理；10 秒后还在才强制结束，
  而且只结束核实过的那一个进程。不往共享控制台广播 Ctrl+C，免得影响别的命令。
- 退出时可以选择停止服务或保留服务；DSH 的输出写在独立的日志文件里，保留服务时不会因为小鲸鱼退出而中断。
- 小鲸鱼自己异常退出时由独立的监护进程恢复，连续三次异常后不再恢复，并提示日志位置；正常退出不重启。
- 余额源显示/隐藏、改名、排序、恢复默认、删除失效源、实时读数；改动立即保存。
- 额度按剩余比例变色；预付余额按金额变色，美元按原版 1:7 的固定比例换算。
- 微信 Clawbot 状态和原版同一个意思：检查账号文件，表示已登录，不代表此刻在线。
- 权限模式跟随 DSH 默认或显式完全访问，下一次启动服务时生效。
- 从启动日志取得登录链接，用浏览器或 Edge 独立窗口打开网页。
- 登录 Windows 时自动启动（默认不启用，在设置里打开）。

## 余额从哪来

和 macOS 版一样：余额来自 DSH 的 `GET /api/model-balance`，由 DSH 里的余额插件提供，小鲸鱼不带这个插件，
自己也不连任何厂商、不碰 API key。没有这个插件时，余额区是空的，其他功能照常。接口的返回格式见
[macOS 文档的「余额从哪来」](../mac/README.md#余额从哪来)。这个接口不需要 DSH 的登录。

## 数据与诊断

- 设置：`%LOCALAPPDATA%\DSHWhale\settings.json`，保存时保留 `.bak`。
- 小鲸鱼日志：`%LOCALAPPDATA%\DSHWhale\logs\whale.log`。
- DSH 输出：DSH 文件夹（设了 `DSH_HOME` 就是它，否则 `%USERPROFILE%\.dsh`）下的 `logs\dsh-web.out.log` 和 `dsh-web.err.log`。
- 开机自启：当前用户 Run 项里的 `DSHWhale`（带 `--autostart`）。

移植早期的测试版叫 DSHWhale-Vibe。新版第一次运行时，会把 `%LOCALAPPDATA%\DSHWhale-Vibe\settings.json`
和名为 `DSHWhale-Vibe` 的开机自启项搬过来；旧版请先退出再运行新版。

托盘程序不读取也不保存模型 API Key。本机登录 token 只从 DSH 的启动输出里读，诊断信息会把 token 遮掉。
接管别处启动、没有对应日志的服务时，可能拿不到它当前的 token，原版也有这个限制。

## 构建与验证

```powershell
.\scripts\build-windows.ps1
.\scripts\test-windows.ps1
.\scripts\test-lifecycle.ps1
```

使用 Windows 自带的 .NET Framework C# 编译器，不需要另装 .NET SDK。
`test-windows.ps1` 在独立的随机端口和测试目录里启动模拟 DSH，测试生命周期、余额接口、重试上限、退出时保留服务、
接管外部实例、正常退出信号、DSH 桌面版检测；对用户自己 3080 上的服务只做只读检测。
`test-lifecycle.ps1` 用 `--no-start` 启动本程序，验证常驻、模拟托盘崩溃后的恢复和正常退出。

`build\DSHWhale.exe --shutdown` 用来干净地关闭小鲸鱼，保留 DSH。
`--snapshot <目录> --demo` 只用于开发时离屏渲染截图，正常使用不要带。

Windows 原生窗口、通知、Edge 网页窗口和 macOS 外观不同，系统交互用的是 Windows 对应的方式。
外部共享控制台里的进程没法保证和 Unix SIGINT 完全一样地退出，必要时仍会强制停止。
可选的插件诊断工具目前支持 DSH 文件夹里的 `bin\dsh-plugin-check.exe` 或 `.js`，不直接执行 macOS 的 shell 脚本。
