# 发布说明

当前版本用于 GitHub Releases 自动发布正文。

## 版本定位

- `v1.1` 对应：**正式版 V1.1**
- `v1.0.1-trial` 为此前试用版记录
- 如后续继续发布，可沿用 `v*` 标签触发自动 Release

## 当前版本功能

- 支持全局快捷键自定义
- 支持区域截图和全屏截图
- 支持滚动截图（自动翻页拼接）
- 支持截图后自动复制到剪贴板
- 支持截图后弹出预览窗口
- 预览窗口支持放大、缩小、100% 还原、`Ctrl + 滚轮` 缩放
- 支持在预览窗口中连续框选区域进行 OCR 识别
- OCR 识别结果会自动追加到输出框，并可复制到剪贴板
- OCR 识别前会自动进行外扩选区、放大与轻量增强，以提升小字和轻微偏移场景下的识别率

## Release 附件

自动发布时会上传以下资产：

- `screen_capture_tool_<tag>.zip`
- `ScreenCaptureTool_CN.exe`
- `run_screen_capture_tool.vbs`

其中压缩包默认包含：

- `ScreenCaptureTool_CN.exe`
- `run_screen_capture_tool.vbs`
- `screen_capture_settings_native_cn.ini`
- `README.md`
- `CHANGELOG.md`
- `使用界面.png`
- `ScreenCaptureTool_CN.ico`

## 使用说明

优先直接运行：`ScreenCaptureTool_CN.exe`

如需静默启动，可运行：`run_screen_capture_tool.vbs`

## 备注

当前仓库以“**源码 + 可直接运行 EXE 一起分发**”方式维护，适合本地使用、备份和持续迭代。
