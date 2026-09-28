#!/bin/bash
# 编译并打包成 build/DSHWhale.app(自签名)。
#
#   scripts/build-app.sh             # 只打包
#   scripts/build-app.sh --install   # 打包并装进 /Applications
#
# 可选环境变量:BUNDLE_ID(默认 io.github.alphainfix.dsh-whale)、VERSION。
# 设置存在这个 bundle id 名下 —— 换了 id,之前的设置就读不到了。
set -euo pipefail
cd "$(dirname "$0")/.."

BUNDLE_ID="${BUNDLE_ID:-io.github.alphainfix.dsh-whale}"
VERSION="${VERSION:-1.1}"
APP=build/DSHWhale.app

swift build -c release
BIN="$(swift build -c release --show-bin-path)/DSHWhale"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/DSHWhale"
cp Sources/DSHWhale/Resources/*.png "$APP/Contents/Resources/"
cp packaging/AppIcon.icns "$APP/Contents/Resources/"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleDisplayName</key>	<string>DSH小鲸鱼</string>
	<key>CFBundleName</key>	<string>DSH小鲸鱼</string>
	<key>CFBundleExecutable</key>	<string>DSHWhale</string>
	<key>CFBundleIconFile</key>	<string>AppIcon</string>
	<key>CFBundleIdentifier</key>	<string>${BUNDLE_ID}</string>
	<key>CFBundlePackageType</key>	<string>APPL</string>
	<key>CFBundleShortVersionString</key>	<string>${VERSION}</string>
	<key>CFBundleVersion</key>	<string>${VERSION}</string>
	<key>LSMinimumSystemVersion</key>	<string>13.0</string>
	<key>LSUIElement</key>	<true/>
	<key>NSHighResolutionCapable</key>	<true/>
</dict>
</plist>
PLIST

# 自签名:没有 Developer ID 也能在本机运行。拷进 bundle 的可执行文件会破坏原有
# 签名,所以打包的最后一步一定是重新签。
codesign --force --sign - "$APP"
echo "已打包: $APP  (bundle id: $BUNDLE_ID)"

if [ "${1:-}" = "--install" ]; then
  rm -rf /Applications/DSHWhale.app
  cp -R "$APP" /Applications/
  echo "已安装到 /Applications/DSHWhale.app"
fi
