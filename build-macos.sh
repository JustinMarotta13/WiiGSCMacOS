#!/bin/bash
cd "$(dirname "$0")/src/WiiGSC.UI" && dotnet publish -c Release -r osx-arm64 --self-contained true && rm -rf ../../WiiGSC.app && mkdir -p ../../WiiGSC.app/Contents/{MacOS,Resources} && cp -r bin/Release/net8.0/osx-arm64/publish/* ../../WiiGSC.app/Contents/MacOS/ && chmod +x ../../WiiGSC.app/Contents/MacOS/WiiGSC.UI && cp Assets/WiiGSC.icns ../../WiiGSC.app/Contents/Resources/ && cat > ../../WiiGSC.app/Contents/Info.plist << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>WiiGSC</string>
    <key>CFBundleDisplayName</key>
    <string>Wii Game Shortcut Creator</string>
    <key>CFBundleIdentifier</key>
    <string>com.wiicrazy.wiigsc</string>
    <key>CFBundleVersion</key>
    <string>2.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>2.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>WiiGSC.UI</string>
    <key>CFBundleIconFile</key>
    <string>WiiGSC.icns</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF
echo "✅ Build complete: WiiGSC.app"
