# DSH 小鲸鱼 🐋

照看 DeepSeek Harness 的桌面小助手：macOS 菜单栏版和 Windows 托盘版。

| 目录 | 内容 |
|---|---|
| `mac/` | macOS 菜单栏应用（Swift）、资源和打包脚本 |
| `windows/` | Windows 托盘应用（C#）、图标、构建脚本和测试 |

## 下载

在 [Releases](https://github.com/Alphainfix/dsh-whale/releases) 下载对应平台的压缩包：

- macOS：`DSHWhale-<版本>-macos.zip`，Apple 芯片和 Intel 通用，macOS 13 起。
- Windows：`DSHWhale-<版本>-windows-x64.zip`，Windows 10/11，需要 Node.js。

两个包都没有经过苹果公证或 Windows 代码签名，第一次打开会被系统拦一下，怎么放行见各自的文档。

## 支持的 DSH

小鲸鱼托管的是用 npm 安装的 `dsh web`（全局安装，或 npx 缓存里的那份）。
DSH 桌面版自己管理服务，它的本机端口不是 3080，两个版本都看不到它的状态和余额。Windows 版默认的「自动」模式下，DSH 文件夹
（`DSH_HOME`，默认 `%USERPROFILE%\.dsh`）的 `profiles` 里只有 `desktop`、没有 `web` 时不另起 `dsh web`；macOS 版不识别桌面版，
3080 空着时照样会去启动 npm 装的 `dsh web`（但不会拿桌面版装的 `dsh` 命令去启动）。

## macOS

要求 macOS 13+ 和 Xcode 命令行工具。从源码构建：

```bash
scripts/build-app.sh --install
scripts/install-launchagent.sh
```

应用打包在 `mac/build/DSHWhale.app`。使用说明见 [macOS 文档](mac/README.md)。

## Windows

要求 Windows 10/11 和 Node.js；不用另装 .NET SDK（用系统自带的 .NET Framework 4 编译器）。从源码构建：

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\scripts\build-windows.ps1
```

双击 `windows\build\DSHWhale.exe` 显示面板；加 `--open` 会先启动服务再打开网页。服务已经在运行时直接复用。

<img src="docs/windows-panel.png" width="360" alt="Windows 面板（演示数据）">

完整说明见 [Windows 文档](windows/README.md)。

## 测试与发布

```powershell
powershell -ExecutionPolicy Bypass -File .\windows\scripts\test-windows.ps1
powershell -ExecutionPolicy Bypass -File .\windows\scripts\package-windows.ps1
```

GitHub Actions 在推到 main、开 PR、推 `v*` tag（或手动运行）时分别构建、测试 macOS 版和 Windows 版；推 `v*` tag 时，两个平台的压缩包会挂到对应的 Release 上。

## License

[MIT](LICENSE)，保留 Alphainfix 的原始版权与许可。Windows 版由 [genius-FXC](https://github.com/genius-FXC) 移植，蓝色鲸鱼图标是生成式图像资源。
