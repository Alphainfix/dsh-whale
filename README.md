# DSH 小鲸鱼 🐋

macOS 菜单栏上的一只小鲸鱼，帮你照看 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness)（DSH）：

- **守着服务**：开机自动拉起 `dsh web`，崩了自动重启；启动失败会发通知告诉你原因。
- **一眼看余额**：DeepSeek、OpenRouter、ChatGPT 订阅……想看哪几个，在设置里勾选。
- **一键打开界面**：自动带上登录 token 打开 DSH 网页，不用再去日志里翻链接。

<img src="docs/panel.png" width="288" alt="小鲸鱼面板">

> **English** — A macOS menu bar companion for DeepSeek Harness. It keeps `dsh web` running
> (auto-start at login, restart on crash, a notification with the reason when startup fails),
> shows the model balances you choose, and opens the web UI with its login token in one click.
> Build with `scripts/build-app.sh --install`; everything else below is in Chinese.

## 安装

需要 macOS 13 以上、Xcode 命令行工具（`xcode-select --install`），以及已经能用的 DSH。

```bash
git clone https://github.com/Alphainfix/dsh-whale.git
cd dsh-whale
scripts/build-app.sh --install      # 编译、打包，装进 /Applications
scripts/install-launchagent.sh      # 可选：开机自动启动
```

然后从「应用程序」里打开 **DSH小鲸鱼**，菜单栏会出现一只鲸鱼。它没有 Dock 图标。

## 使用

点一下菜单栏的鲸鱼：

- **开启 / 关闭 / 重启服务**，以及**打开 DSH 界面**。
- 服务是按 3080 端口认的：你在终端里自己运行的 `dsh web`，小鲸鱼一样看得见、管得了。
- 退出小鲸鱼时，它会问你要不要把服务一起停掉。

「打开 DSH 界面」会优先用 `~/Applications/DSH.app`（用 Safari「添加到程序坞」做的网页 App，独立窗口）；
没有的话就用默认浏览器。

## 设置

面板右下角的「设置…」（⌘,）：

<img src="docs/settings.png" width="460" alt="设置窗口">

- **余额显示**：勾选要显示的余额，名字可以直接改，右边的箭头调整顺序。
- **显示微信 Clawbot 状态**：没用 [wechat-clawbot](https://github.com/Alphainfix/wechat-clawbot) 的话可以关掉这一行。
- **启动 DSH 的权限模式**：默认跟随 DSH（改文件、跑命令前会先问你）。选「完全访问」后，
  小鲸鱼拉起的 DSH 可以读写整台电脑，而且不再弹审批 —— 确定需要再开。

## 余额从哪来

小鲸鱼自己不连任何厂商，也不碰你的 API key。余额来自 DSH 的 `GET /api/model-balance`，
这个接口由 DSH 里的余额插件提供；没有这个插件的话，余额区就是空的，其他功能照常。

想自己提供余额的话，接口返回这个形状就行（每个源一项，键就是设置里看到的 id）：

```json
{
  "ok": true,
  "providers": {
    "deepseek": { "ok": true, "label": "DeepSeek", "kind": "prepaid", "currency": "CNY", "remaining": 86.4, "limit": null },
    "codex":    { "ok": true, "label": "ChatGPT 5h 余量", "kind": "quota", "currency": "%", "remaining": 72, "limit": 100 }
  }
}
```

`kind` 是 `prepaid`（预付，没有上限）或 `quota`（额度，有上限）。有上限的按剩余比例变色，没上限的按金额变色。

## 常见问题

**余额一直显示「—」**：DSH 没在运行，或者 DSH 里没有提供余额接口的插件。

**怎么重启 DSH**：面板里点「重启服务」。或者直接结束那个进程，小鲸鱼 20 秒内会把它拉起来：

```bash
kill $(lsof -nP -iTCP:3080 -sTCP:LISTEN -t)
```

**DSH 启动失败**：小鲸鱼会发通知，附上 DSH 自己报的错误。完整输出在 `~/.dsh/logs/dsh-web.err.log`。
刚启动就退出的情况（配置写错、插件加载不了）连续 3 次之后它就不再自动重试，修好之后在面板里点「开启服务」。

**卸载**：

```bash
scripts/install-launchagent.sh --uninstall
rm -rf /Applications/DSHWhale.app
defaults delete io.github.alphainfix.dsh-whale   # 清掉设置
```

## 它是怎么工作的

- **找 node**：先用 nvm 里最新的版本，再找 Homebrew 和系统路径。DSH 带有原生模块，和 node 大版本绑定，
  所以要用装 DSH 时的那个 node。
- **找 dsh**：全局安装优先，否则用 npx 缓存里最近更新的那一份。
- **启动**：`node <dsh> web --no-open`，工作目录是你的主目录，输出追加到 `~/.dsh/logs/`。
- **看门**：每 20 秒看一次端口。空了、而且不是你手动停的，就重新拉起。

## 开发

```bash
swift build                                        # 调试构建
scripts/build-app.sh                               # 打包到 build/DSHWhale.app
build/DSHWhale.app/Contents/MacOS/DSHWhale --snapshot /tmp/shots --demo   # 用示例数据渲染面板和设置页
```

## License

MIT
