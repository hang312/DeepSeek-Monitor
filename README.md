[README.md](https://github.com/user-attachments/files/28226739/README.md)
<p align="center">
  <h1>DeepSeek Monitor</h1>
  <p>桌面悬浮窗，实时监控 DeepSeek API 余额与用量</p>
  <p><em>A desktop widget for real-time DeepSeek API balance & usage monitoring</em></p>
</p>

---

## 功能 | Features

- **余额卡片** — 实时显示 API 账户总余额
- **累计充值** — 自动追踪累计充值总额，后续充值自动累加
- **Token 消耗** — 基于消费金额估算 Token 用量，换算率可调
- **消耗进度条** — 直观展示已消耗金额占比（绿 < 50% < 橙 < 80% < 红）
- **30 秒自动刷新** — 开发时挂在副屏/角落，随时掌握用量
- **无边框置顶** — 半透明深色主题，可拖拽，不占工作空间
- **零依赖** — 纯 C# WinForms 编译，Windows 原生运行

---

## 截图 | Preview

```
┌──────────────────────────────┐
│  ● DeepSeek            =  X │
├──────────────────────────────┤
│  ┌──────────────────────┐   │
│  │       余额            │   │
│  │      36.50           │   │
│  │       CNY            │   │
│  └──────────────────────┘   │
│  ┌──────────┐ ┌──────────┐  │
│  │ 累计充值  │ │ 已用Token │  │
│  │ 100.00   │ │  2.1M    │  │
│  └──────────┘ └──────────┘  │
│  ┌──────────────────────┐   │
│  │ 已消耗 63.50    63%  │   │
│  │ ██████████░░░░░░░░░  │   │
│  └──────────────────────┘   │
│  14:30:25             28s   │
└──────────────────────────────┘
```

---

## 使用方法 | Usage

1. 下载 `DeepSeekMonitor.exe`
2. 双击运行，首次打开弹出设置窗口
3. 输入 DeepSeek API Key（`sk-` 开头）
4. 可选：输入累计充值总额校准消耗数据
5. 可选：调整 Token 换算率（默认 500K/CNY）

> 在 DeepSeek 控制台 → API Keys 页面获取 Key

---

## 编译 | Build

```bash
csc /target:winexe /out:DeepSeekMonitor.exe DeepSeekMonitor.cs /reference:System.Web.Extensions.dll
```

要求 .NET Framework 4.x（Windows 10/11 自带）。

---

## 技术栈 | Tech Stack

- C# (.NET Framework 4.x)
- Windows Forms
- DeepSeek Balance API

---

## 注意事项 | Notes

- API Key 和配置数据仅存储在本地 `deepseek-monitor-config.json` 文件中，不会上传到任何服务器
- Token 用量为基于消费金额的估算值，非精确统计
- 充值后程序会自动检测并更新累计充值额，无需手动操作
