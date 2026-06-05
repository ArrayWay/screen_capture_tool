# GitHub Releases 文案草稿

本文档用于给当前项目在 GitHub 上补充 Release 文案。

当前版本按你的要求统一记为：**试用版 V1.0**。

---

## Release 建议

- **Tag**：`v1.0-trial`
- **Release title**：`试用版 V1.0`
- **Target**：`main`
- **Set as latest release**：是
- **Pre-release**：建议勾选

> 说明：如果你希望版本号更简洁，也可以直接使用 tag：`v1.0`，但从发布语义上看，`v1.0-trial` 更能体现“试用版”状态。

---

## 可直接粘贴到 GitHub Release 的正文

### 简版

`screen_capture_tool` 的首个 GitHub 发布版本，当前定义为 **试用版 V1.0**。

本版本已具备本地日常截图所需的核心能力，包括区域截图、全屏截图、滚动截图、预览缩放以及 OCR 文字提取。

### 主要功能

- 支持全局快捷键自定义
- 支持区域截图和全屏截图
- 支持滚动截图（自动翻页拼接）
- 支持导出 `png`、`jpg`、`bmp`、`gif`、`tiff`
- 支持开机启动
- 支持托盘常驻
- 支持截图后自动复制到剪贴板
- 支持截图后弹出预览窗口
- 预览窗口支持放大、缩小、100% 还原、`Ctrl + 滚轮` 缩放
- 支持在预览窗口中连续框选区域进行 OCR 识别
- OCR 结果支持追加输出并复制到剪贴板
- OCR 识别前增加外扩选区、放大和轻量增强，提高小字识别稳定性

### 包含文件

- `ScreenCaptureTool_CN.exe`
- `run_screen_capture_tool.vbs`
- `ScreenCaptureTool_CN.cs`
- `screen_capture_settings_native_cn.ini`
- `ScreenCaptureTool_CN.ico`

### 使用说明

优先直接运行：`ScreenCaptureTool_CN.exe`

如需静默启动，可运行：`run_screen_capture_tool.vbs`

### 当前说明

这是一个以“**源码 + 可直接运行 EXE 一起分发**”方式维护的 Windows 本地截图工具试用版本，适合个人本地使用、备份和持续迭代。

如在 OCR 识别、滚动截图或特殊窗口兼容性上遇到问题，后续会继续按实际使用情况逐步优化。

---

### 详细版

这是 `screen_capture_tool` 项目的首个 GitHub Release，当前版本定义为：**试用版 V1.0**。

项目面向 Windows 本地使用场景，目标是提供一个轻量、直接可用、支持 OCR 和滚动截图的桌面截图工具。

#### 本版本已具备的能力

- 全局快捷键触发截图
- 区域截图 / 全屏截图
- 滚动截图（适合网页、文档、聊天记录等纵向内容）
- 截图后自动复制到剪贴板
- 截图后弹出预览窗口
- 预览窗口缩放查看（放大、缩小、100%、`Ctrl + 滚轮`）
- 预览窗口 OCR 框选识别
- 连续 OCR 框选，多段文本追加输出
- OCR 结果一键复制
- 开机启动与托盘常驻
- 多种图片格式导出

#### OCR 相关增强

当前版本已对 OCR 体验做了基础增强：

- 支持连续 OCR 框选，不需要每识别一次都重新进入模式
- OCR 结果改为追加输出，更适合连续摘取多段文字
- 识别前自动外扩选区，减少框选过紧导致的漏字
- 识别前自动放大并做轻量图像增强，提高小字号和轻微偏移场景下的识别率

#### 适用场景

- 日常桌面截图
- 文档留存
- 网页长图截取
- 聊天记录保存
- 局部文字提取

#### 注意事项

- 滚动截图依赖目标窗口能正确响应 `PageDown`
- OCR 底层依赖 Windows 系统 OCR，复杂背景、模糊字体、小字号场景下仍可能存在错字或漏字
- 当前仓库保留源码与 EXE，便于直接运行和版本备份

#### 后续方向

后续版本会根据实际使用反馈，继续优化：

- OCR 识别稳定性
- 滚动截图兼容性
- 文档与发布说明
- 代码结构维护性

---

## GitHub Release 附件建议

建议在 Release 中附带或保留以下资产：

- `ScreenCaptureTool_CN.exe`
- `run_screen_capture_tool.vbs`
- 可选：打包一个压缩包，例如 `screen_capture_tool_v1.0_trial.zip`

如果你准备上传压缩包，建议包内至少包含：

- `ScreenCaptureTool_CN.exe`
- `run_screen_capture_tool.vbs`
- `screen_capture_settings_native_cn.ini`
- `README.md`
- `使用界面.png`

---

## 后续版本建议命名

- `v1.0-trial`：试用版 V1.0
- `v1.1`：后续功能增强版
- `v1.2`：稳定性优化版

如果后续你希望，我可以继续直接帮你补一版：

1. 更适合 GitHub 展示的正式 Release 正文
2. 对应的版本标签命名方案
3. 下一版 `CHANGELOG.md` 的版本分段
