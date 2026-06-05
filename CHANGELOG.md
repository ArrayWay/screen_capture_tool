# 更新日志

本文档用于记录 `screen_capture_tool` 项目的可见变更，便于后续本地维护、版本回溯与远程备份核对。

## [1.0] - 当前基线版本

### 已确认能力

- 提供可直接运行的主程序：`ScreenCaptureTool_CN.exe`
- 提供源码文件：`ScreenCaptureTool_CN.cs`
- 提供静默启动脚本：`run_screen_capture_tool.vbs`
- 提供配置文件：`screen_capture_settings_native_cn.ini`
- 支持全局快捷键自定义
- 支持区域截图和全屏截图
- 支持滚动截图（自动翻页拼接）
- 支持导出 `png`、`jpg`、`bmp`、`gif`、`tiff`
- 支持开机启动
- 支持托盘常驻
- 支持截图后自动复制到剪贴板
- 支持截图后弹出预览窗口
- 预览窗口支持放大、缩小、100% 还原、`Ctrl + 滚轮` 缩放
- 支持在预览窗口中框选区域进行 OCR 识别
- OCR 识别结果可复制到剪贴板

### 仓库整理

- 建立独立 Git 仓库并使用 `main` 分支
- 保留“源码 + 可直接运行 EXE 一起分发”的当前项目形态
- 补充 `.gitignore`，忽略常见构建产物和系统噪音文件
- 优化 `README.md` 结构，增强 GitHub 展示效果
- 增加界面预览图片：`使用界面.png`

---

## 后续记录建议

后续新增版本时，建议按以下格式追加：

```md:screen_capture_tool/CHANGELOG.md
## [版本号] - YYYY-MM-DD

### 新增
- ...

### 修复
- ...

### 调整
- ...
```
