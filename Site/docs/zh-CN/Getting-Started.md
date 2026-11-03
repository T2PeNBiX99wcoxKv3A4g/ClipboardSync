# 入门

ClipboardSync 让已配对设备的剪贴板保持同步。在一台设备上复制文本、链接、图片、文件或文件夹，其他设备也会收到。所有通信都由设备之间通过加密会话直接完成。

## 需要什么

- Windows 或 Linux，x64。构建是自包含的，不需要安装 .NET 运行时。
- 两台能互相连接的设备。TCP 45456 用于配对和剪贴板通信，UDP 45457 只用于局域网发现。
- Linux 需要 WebKitGTK 4.1 和 AppIndicator 库。没有 AppIndicator 时应用仍可运行，只是没有托盘图标。

## 启动

运行 `ClipboardSync.exe`（或 Linux 二进制文件）。关闭窗口后应用会留在托盘。同一时间只运行一个实例，再次启动会唤回已有窗口。

## 配对第一台设备

打开 **设备 → 配对设备**。选择发现的设备，或输入地址，然后比对两边屏幕显示的配对码。参见 [配对](Pairing)。

## 复制内容

在任意应用中复制文本、URL、图片、文件或文件夹。项目会出现在这台设备下方，发送到已配对设备，并应用到它们的剪贴板。共享内容和离线时的行为参见 [同步](Syncing)。

## 数据位置

| 内容                   | Windows                        | Linux                          |
|------------------------|--------------------------------|--------------------------------|
| 设置、身份、已配对设备 | `%APPDATA%\ClipboardSync`      | `~/.config/ClipboardSync`      |
| 历史和图片缓存         | `%LOCALAPPDATA%\ClipboardSync` | `~/.local/share/ClipboardSync` |
| 文件 blob 和视图       | `%LOCALAPPDATA%\ClipboardSync\clipboard` | `~/.local/share/ClipboardSync/clipboard` |
| 日志                   | `%APPDATA%\ClipboardSync\logs` | `~/.config/ClipboardSync/logs` |

身份密钥就是配对时对方记录的公钥，因此该文件夹应只允许你的账户访问。

## 接下来

- [配对](Pairing) — 发现、地址和配对码。
- [同步](Syncing) — 面板、传输状态和重试。
- [安全性](Security) — 加密覆盖的范围。
