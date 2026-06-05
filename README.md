# Windows 截图工具

- 主程序：`ScreenCaptureTool_CN.exe`
- 静默启动：`run_screen_capture_tool.vbs`
- 源码：`ScreenCaptureTool_CN.cs`
- 配置文件：`screen_capture_settings_native_cn.ini`

功能：

- 支持全局快捷键自定义
- 支持区域截图和全屏截图
- 支持滚动截图（自动翻页拼接）
- 支持 `png`、`jpg`、`bmp`、`gif`、`tiff`
- 支持开机启动
- 支持托盘常驻
- 支持截图后自动复制到剪贴板
- 截图后弹出预览窗口
- 预览窗口支持放大、缩小、100% 还原、`Ctrl + 滚轮` 缩放
- 支持在预览窗口中框选区域进行 OCR 识别
- OCR 识别结果可复制到剪贴板

说明：

- 版本 `1.0` 视为此前确认可用的基础版
- 当前目录仅保留最新版本文件，后续如需回退，以 `1.0` 需求集合作为基线继续调整
- 滚动截图依赖目标窗口能响应 `PageDown` 翻页，适合网页、文档、聊天记录等纵向内容
