# Codex Window Shot

Windows 下按窗口截图的小工具，附带一个给 Codex 使用的技能。截图只取目标窗口自身的画面，即使它被其他窗口遮挡，也不会把上层窗口带进图片。

## 功能

- 按窗口捕获，不依赖整屏截图或框选
- 实时预览、保存 PNG、复制到剪贴板
- 全局快捷键 `Ctrl+Alt+Shift+S`，在其他应用里直接截当前活动窗口
- 命令行列出窗口、按句柄或标题截图
- 附带 Codex skill，自动按窗口截图

## 目录

```text
skill/
  SKILL.md                  Codex 技能说明
  scripts/capture-window.ps1  调用脚本
  bin/WindowShot.exe       预编译工具
  agents/openai.yaml       技能界面信息
src/WindowShot/            C# / WPF 源码
```

## 直接使用

1. 运行 `skill\bin\WindowShot.exe`。
2. 在左侧选择目标窗口，右侧会显示实时预览。
3. 点“截图”，图片会保存到 `图片\WindowShot` 并复制到剪贴板。
4. 工具运行时，在其他应用里按 `Ctrl+Alt+Shift+S` 会直接截取当前活动窗口；如果工具自己在前台，则截取左侧选中的窗口。

运行需要 .NET 8 Desktop Runtime。

## 安装为 Codex 技能

把 `skill` 目录复制到：

```text
%USERPROFILE%\.codex\skills\window-shot
```

新开对话后 Codex 会发现这个技能，也可以用 `$window-shot` 显式调用。

技能脚本示例：

```powershell
& "$env:USERPROFILE\.codex\skills\window-shot\scripts\capture-window.ps1" `
  -Match "Notepad" `
  -Out ".\shot.png"
```

脚本会先列出窗口，按标题或进程名选择目标，再按窗口句柄截图，最后返回 JSON 结果。

窗口枚举和截图需要访问交互式桌面。在 Codex 沙箱里运行可能看不到任何窗口，需要提升权限或使用有桌面访问权限的环境。

## 命令行

```powershell
# 列出窗口，写入 JSON
skill\bin\WindowShot.exe --list windows.json

# 按句柄截图
skill\bin\WindowShot.exe --capture 0x12345 shot.png

# 按标题或进程名截图
skill\bin\WindowShot.exe --capture-title "记事本" shot.png
```

`WindowShot.exe` 是 GUI 子系统程序。脚本调用 `--list` 时建议使用 `Start-Process -Wait`，或等待 JSON 文件出现后再读取。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet publish src/WindowShot/WindowShot.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -o skill/bin
```

Release 配置关闭了调试符号，避免把本机编译路径写进 exe。

## 实现说明

- 使用 Win32 `PrintWindow` 和 `PW_RENDERFULLCONTENT` 捕获窗口自身表面。
- 过滤工具窗口、 cloak 窗口和无效窗口；最小化窗口会被明确拒绝。
- 使用 Per-Monitor V2 DPI 感知，预览以约 180 ms 的间隔刷新。
- 不使用整屏或区域截图作为兜底，避免重新引入遮挡窗口。
- 部分虚拟桌面、远程会话或纯 GPU 渲染窗口不支持窗口级捕获，可能返回黑图。

## 限制

- 最小化窗口无法截图，恢复窗口后再截。
- 受保护内容、锁屏/安全桌面无法截取。
- 少数游戏或硬件加速窗口可能只返回黑图。

## 隐私

- 工具不发起网络请求。
- 只截取用户选择的窗口，不遍历保存其他窗口内容。
- 设置保存在 `%LOCALAPPDATA%\WindowShot`。
- 测试截图不要提交到仓库。
