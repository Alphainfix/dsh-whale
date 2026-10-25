# DSH 小鲸鱼 · Vibe Windows 版

本地移植分支：`windows-port`。基于 Orbit-Labs-AI/dsh-whale 的提交
`dd6bec544ac47435d207ca6f676802ee58bc73d0`，保留原版 Swift 源码、资源、Git 历史及 MIT 许可证。
Windows 实现位于 `src/`。原版使用说明保留在 `../mac/README.md`。

## 使用

从源码运行下面的命令时，请先进入 `windows` 目录。

双击 `启动小鲸鱼.vbs`，或者运行 `build/DSHWhale-Vibe.exe`。
如果使用发布压缩包，解压后直接运行同目录下的 `DSHWhale-Vibe.exe`，保留旁边的 `whale.png`。
小鲸鱼常驻 Windows 系统托盘，点击鲸鱼显示面板，右键可打开设置或退出。
面板失去焦点会隐藏，这是收起面板，不是进程退出。
双击小鲸鱼快捷方式会显示控制面板；已在托盘运行时会唤出已有面板。
「启动并打开 DSH」会先启动未运行的服务，再打开网页；运行中则显示「打开 DSH 界面」。
启动参数 `--panel` 显示面板，`--open` 直接启动并打开 DSH。
演示参数 `--demo` 不连接、启动或停止真实 DSH，也不会定时自动退出。

无需重新安装现有的 DSH。程序自动查找 PATH / npm 全局安装 / npx 缓存，解析
`@deepseek-ai/dsh/package.json` 的 `bin.dsh` 后调用 Node，缓存哈希没有写死。
程序路径和工作目录可以在设置中分别指定。首次使用时，如果用户 Documents 下存在
`deepseek-harness/default-workspace`，会选用它；否则工作目录为用户主目录。
缓存被删除后，需要先重新运行 npx 恢复 DSH 安装；小鲸鱼本身不会擅自安装或升级 DSH。

与另一版本比较时，一次只启用一个服务守护程序。它们管理同一个 3080 端口，
一方主动停服时，另一方的看门机制可能把它重新启动。

## 已移植的行为

- 服务开启、关闭、重启；识别 3080 端口上已有的 DSH；HTTP 401 也算服务存活。
- 20 秒检查；手动停止后不再自动启动；连续三次短暂启动失败后停止重试。
- 服务操作串行化，启动中的进程不会因端口尚未就绪被重复启动。
- 先对可核实且使用独立控制台的进程发送 Ctrl+C，让 Node/DSH 执行 SIGINT 清理；
  超时才强制结束。对共享控制台不广播 Ctrl+C，避免影响其他命令。
- 退出时可停止服务或保留服务；日志使用子进程独立文件句柄，保留服务时不会因父进程退出关闭输出管道。
- 小鲸鱼自身异常退出由独立监护进程恢复，连续三次异常后停止恢复并提示日志位置；正常退出不重启。
- 余额源显示/隐藏、改名、排序、恢复默认、失效源删除、实时读数；更改即时保存。
- 配额按比例判断颜色；预付余额按金额判断，USD 使用原版的 1:7 固定颜色换算规则。
- 微信 Clawbot 状态保持原版语义：检查账号 JSON 文件，表示已登录，不代表实时在线。
- 权限模式跟随 DSH 默认或显式完全访问，下一次启动服务时生效。
- 从启动日志取得登录链接；给余额请求建立认证会话；浏览器或 Edge 独立窗口打开网页。
- 登录 Windows 时自动启动（默认不启用，在设置中选择）。

## ChatGPT OAuth 与真实余额

本版附带 `../shared/plugins/` 下的 DSH 插件（发行包放在 `plugins/`）。首次使用、移到新目录或解压发行包后运行
`powershell -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1`，会自动找到现有 DSH，
备份并修改 web profile，保留其他配置，不重装 DSH。插件文件应留在安装位置。

小鲸鱼设置页或托盘右键的「登录 ChatGPT」打开本机授权页面。
首次先用「打开 DSH 界面」取得浏览器的本机会话，再打开登录页面。
选择 Browser login，打开 OpenAI 授权页并自行完成登录；也支持 DSH 提供的 Device code login。
成功后在 DSH 模型选择器选择 ChatGPT (OAuth) 下的模型。原有默认模型不自动替换。

授权使用已安装 DSH 的 `llm-pi-ai/openai-codex` 原生流程，凭据存于 DSH，由其负责调用 GPT
与刷新令牌。原版 mac 小鲸鱼只读取余额接口，并没有实现这项 OAuth 功能。
已验证的 DSH 0.2.0-rc.2 模型页面缺少 OAuth 按钮，所以本版补上授权 UI；不复制或修改 npx 包。

- DeepSeek：通过 DSH 的 `DEEPSEEK_API_KEY` 查询官方余额接口，已实测成功。
- OpenRouter：优先账户 credits；普通 Key 无权限时读取该 Key 的限额并明确标注。
  无限额 Key 不等于账户余额为零；账户余额需要相应 Management Key。
- ChatGPT：显示 DSH 登录账号的订阅用量窗口，不是美元余额。使用已安装 Codex CLI 的隔离
  app-server 查询，临时目录不沿用 Codex 个人配置，凭据只通过 stdin 传递，不写入参数或日志。
  该外部令牌接口依赖 Codex 版本且属于实验能力；失败时显示不可用，不影响 DSH 调用 GPT。
  未登录、授权过期或没返回用量时不会显示模拟百分比。

余额接口与登录页面注册在 DSH 已认证的 connection 路由中；每 60 秒刷新供应商余额。
OpenRouter 密钥请在本机 DSH 设置中输入，不要发送到聊天。真实 GPT 调用与 ChatGPT 用量
已完成 OAuth 集成验证，并通过 DSH 原生适配器调用 `gpt-6-luna` 返回 `OK`；
ChatGPT 订阅用量查询已实测成功。窗口类型由服务返回值决定，不固定假设总有 5 小时和周两种额度。

## 数据与诊断

- 设置：`%LOCALAPPDATA%\DSHWhale-Vibe\settings.json`，保存时保留 `.bak`。
- 小鲸鱼日志：`%LOCALAPPDATA%\DSHWhale-Vibe\logs\whale.log`。
- DSH 输出：`%USERPROFILE%\.dsh\logs\dsh-web.out.log` 和 `dsh-web.err.log`。
- 自启动项：当前用户 Run 项中的 `DSHWhale-Vibe`，不覆盖其他版本。

托盘程序不读取或保存模型 API Key；DSH 插件通过 DSH 凭据服务访问查询所需的凭据。
本机登录 token 只从 DSH 启动输出读取；诊断信息会遮蔽 token。
小鲸鱼和余额插件是独立组件。余额接口不存在、未认证或供应商查询失败时，显示不可用，
不会生成虚假余额。接管外部启动且没有相应日志的服务，可能无法取得其当前 token；
原版也有这个限制。

## 构建与验证

```powershell
.\scripts\build-windows.ps1
.\scripts\test-windows.ps1
.\scripts\test-lifecycle.ps1
node --test .\tests\balances.test.mjs
```

使用 Windows 自带的 .NET Framework C# 编译器，无需额外安装 .NET SDK。
`test-windows.ps1` 在独立随机端口和测试目录中启动模拟 DSH，测试生命周期、认证、余额、
重试限制、退出保留服务、外部实例接管、正常退出信号；对用户的 3080 服务只作只读检测。
`test-lifecycle.ps1` 以 `--no-start` 启动本版本，验证常驻、模拟托盘崩溃恢复及正常退出。

`build/DSHWhale-Vibe.exe --shutdown` 用于干净地关闭本版本，保留 DSH，不影响另一个版本。
`--snapshot <目录> --demo` 仅供开发离屏渲染验证；正常使用不要带此参数。

Windows 原生窗口、通知、Edge 网页窗口与 macOS 外观不同；系统交互采用 Windows 对应方式。
外部共享控制台进程无法保证与 Unix SIGINT 完全相同的退出方式，必要时仍使用强制停止。
可选的插件诊断工具目前支持 `.dsh/bin/dsh-plugin-check.exe` 或 `.js`，不直接执行 macOS shell 脚本。
