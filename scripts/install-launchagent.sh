#!/bin/bash
# 开机自动启动小鲸鱼(LaunchAgent)。崩溃会被拉回来;从菜单里正常退出则保持退出。
#
#   scripts/install-launchagent.sh              # 安装并立即启动
#   scripts/install-launchagent.sh --uninstall  # 移除
#
# 用 LaunchAgent 而不是「系统设置 → 登录项」:登录项在系统升级后可能被停用,
# 或者在 app 挪过位置之后指向一个不存在的路径。
set -euo pipefail

LABEL="${LABEL:-io.github.alphainfix.dsh-whale}"
APP="${APP:-/Applications/DSHWhale.app}"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"
LOGS="$HOME/.dsh/logs"
DOMAIN="gui/$(id -u)"

if [ "${1:-}" = "--uninstall" ]; then
  launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
  rm -f "$PLIST"
  echo "已移除 $PLIST"
  exit 0
fi

if [ ! -x "$APP/Contents/MacOS/DSHWhale" ]; then
  echo "找不到 $APP,先运行 scripts/build-app.sh --install" >&2
  exit 1
fi

mkdir -p "$LOGS" "$(dirname "$PLIST")"
cat > "$PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>Label</key>	<string>${LABEL}</string>
	<key>ProgramArguments</key>
	<array>
		<string>${APP}/Contents/MacOS/DSHWhale</string>
	</array>
	<key>RunAtLoad</key>	<true/>
	<key>KeepAlive</key>
	<dict>
		<key>SuccessfulExit</key>	<false/>
	</dict>
	<key>StandardOutPath</key>	<string>${LOGS}/whale.out.log</string>
	<key>StandardErrorPath</key>	<string>${LOGS}/whale.err.log</string>
</dict>
</plist>
PLIST

launchctl bootout "$DOMAIN/$LABEL" 2>/dev/null || true
launchctl bootstrap "$DOMAIN" "$PLIST"
echo "已安装并启动: $PLIST"
