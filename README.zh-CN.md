<div align="center">
  <img src="icon.svg" alt="" width="120">
  <h1>ClipboardSync</h1>
  <p><strong>复制一次，粘贴到你的每一台设备。</strong></p>
  <p>
    <a href="https://t2penbix99wcoxkv3a4g.github.io/ClipboardSync/">网站</a> ·
    <a href="https://t2penbix99wcoxkv3a4g.github.io/ClipboardSync/#/docs/Getting-Started">文档</a>
  </p>
  <p>
    <a href="README.md">English</a> ·
    <a href="README.ja.md">日本語</a> ·
    <a href="README.zh-TW.md">繁體中文</a>
  </p>
</div>

ClipboardSync 让已配对设备共用同一块剪贴板面板。在一台设备上复制文本、链接、图片、文件或文件夹，
其他设备就会显示出来，可以直接粘贴。所有设备都通过加密会话直接通信，不需要账号，也没有中间
服务器。

## 为什么需要

在台式机和笔记本之间传递链接或截图，不应该需要发给自己、打开聊天软件，或者信任云端剪贴板。
ClipboardSync 只需把自己的设备配对一次，之后就在你自己的网络中保持同一块面板，剪贴板内容在
设备之间端到端加密。

## 功能

- **共享面板** — 所有已配对设备显示同一份历史记录，项目按来源设备分组，也包括本机。新的复制会
  发送给所有对端；对端保存项目、应用到自己的剪贴板，并在其他对端仍缺少该项目时继续转发。
- **文本与链接** — 复制文本或 URL 时会发送到其他设备，并应用到它们的剪贴板。
- **图片** — 复制的图片会以 PNG 发送、在面板上预览，并自动应用到剪贴板。大小上限可在 1 到
  32 MB 之间设置（默认 10 MB）。
- **文件和文件夹** — 复制的内容会递归遍历，每个文件保存为按 SHA-256 内容寻址的 blob。对端只拉取
  缺少的范围，因此重新连接后会从停下的位置继续，相同内容只占一份 blob。收到的项目会用硬链接
  （不支持时复制）还原，并以普通文件和文件夹放回剪贴板。
- **直接连接** — 设备通过 UDP 在局域网内找到彼此，并通过 TCP 通信。不经过第三方，无法广播的
  网络可以手动填写地址。
- **加密会话** — 每台设备都有长期的 Ed25519 身份密钥。每次连接使用新的 X25519 临时密钥，
  由 HKDF-SHA256 推导会话密钥，每个帧都用 ChaCha20-Poly1305 加密，并以握手记录作为关联数据。
- **配对码** — 两边屏幕会从握手推导出同一组短码，让你在信任设备前先比对。配对码不会作为密码
  发送。
- **每个项目的传输状态** — 已同步、发送中、接收中、等待中或失败，并显示有多少对端持有该项目。
- **最终一致** — 定期核对会重发尚未被确认的项目；再次配对会把当前面板补发过去；曾经离线的设备
  在恢复后会自动补齐。

## 技术栈

- **基于 .NET 10 的 C#** — 剪贴板监视与写入、设备发现、TCP 会话、配对、加密、历史、托盘和设置。
- **React 19 + TypeScript**，由 **Vite** 打包，使用 **styled-components** 编写样式 — 界面。它只与
  C# 侧交换 JSON 消息，自身不处理逻辑。
- **Photino.NET** — 桌面外壳。它用操作系统自带的 Web 视图显示界面，因此 Web 视图是运行时依赖：
  Windows 使用 WebView2，Linux 使用 WebKitGTK。
- **BouncyCastle** — Ed25519、X25519 和 ChaCha20-Poly1305。
- **SQLite** — 剪贴板历史、传输状态和文件 manifest。

## 运行

已发布的构建是 **自包含** 的：内置 .NET 运行时，不需要另外安装。操作系统仍需提供以下内容。

同时发布四种软件包格式：

| 文件                                           | 作用                                                                                                                                        |
|------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------|
| `ClipboardSync-<version>-win-x64.msi`          | 当前用户的 Windows 安装程序：安装到 `%LOCALAPPDATA%\Programs\ClipboardSync`，添加开始菜单和桌面快捷方式，不会请求管理员权限。               |
| `clipboardsync-<version>-1.fcXX.x86_64.rpm`    | Fedora 软件包：`sudo dnf install ./clipboardsync-<version>-*.rpm`。安装到 `/opt/ClipboardSync`，并提供 `clipboardsync` 命令、桌面项和图标。 |
| `clipboardsync_<version>_amd64.deb`            | Debian/Ubuntu 软件包：`sudo apt install ./clipboardsync_<version>_amd64.deb`。布局与 RPM 相同。                                             |
| `clipboardsync-<version>-1-x86_64.pkg.tar.zst` | Arch Linux 软件包：`sudo pacman -U clipboardsync-<version>-1-x86_64.pkg.tar.zst`。布局与 RPM 相同。                                         |

### Windows

1. Microsoft Edge **WebView2 运行时**。Windows 10 和 11 通常已经内置；如果没有，请使用
   [Microsoft 的页面](https://developer.microsoft.com/microsoft-edge/webview2/)。
2. Windows 10 或更高版本，x64。
3. 构建没有代码签名，第一次启动时 Windows 可能会提示警告。

### Linux

1. 用于 GTK 的 Web 视图。
2. AppIndicator 库，仅用于托盘图标。没有它应用仍可运行，只是没有托盘，主窗口会一直显示。
3. 自包含构建所需的 .NET 运行时库（ICU、OpenSSL、zlib、krb5、fontconfig、freetype）会由软件包
   带入。普通压缩包则需要手动安装。

| 发行版          | 软件包                                                                       |
|-----------------|------------------------------------------------------------------------------|
| Fedora          | `sudo dnf install webkit2gtk4.1 libayatana-appindicator-gtk3 libnotify`      |
| Debian / Ubuntu | `sudo apt install libwebkit2gtk-4.1-0 libayatana-appindicator3-1 libnotify4` |
| Arch Linux      | `sudo pacman -S webkit2gtk-4.1 libayatana-appindicator`                      |
| 其他            | 寻找提供下列库的软件包                                                       |

```
libwebkit2gtk-4.1.so.0          libjavascriptcoregtk-4.1.so.0
libgtk-3.so.0                   libglib-2.0.so.0
libayatana-appindicator3.so.1   （或较旧的 libappindicator3.so.1）
```

缺少 Web 视图库时，启动会显示 `Unable to load shared library 'Photino.Native'`。要找出提供缺失
soname 的软件包，可以询问包管理器，例如 `dnf provides 'libwebkit2gtk-4.1.so.0'`、
`apt-file search libwebkit2gtk-4.1.so.0` 或 `pacman -F libwebkit2gtk-4.1.so.0`。

## 首次运行

1. **启动两台设备**，确保它们在同一网络中能互相连接。TCP 45456 用于配对和剪贴板通信；UDP
   45457 只用于发现提示。
2. **配对它们**：打开 **设备 → 配对设备**，选择发现的设备或输入地址，然后比对两边屏幕显示的
   配对码。配对会记录彼此的公开密钥，并证明双方确实拥有该密钥。
3. **复制内容**。项目会出现在两边的面板上，并应用到对方的剪贴板。

完整指南见 [文档](https://t2penbix99wcoxkv3a4g.github.io/ClipboardSync/#/docs/Getting-Started)。

## 构建

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 20 或更高版本，用于构建界面
- `ClipboardSync/Libs` 中的共享 DLL 和前端桥接包，通过 `build-and-copy.ps1` 从单独的
  [PhotinoNETUtils](../PhotinoNETUtils) 项目复制

## 构建与运行

### 开发

界面由 Vite 提供，因此需要同时启动两边：

```bash
# 终端 1 — 界面位于 http://127.0.0.1:3300
cd ClipboardSync/UserInterface
npm install
npm run dev

# 终端 2 — 桌面应用
dotnet run --project ClipboardSync
```

调试构建会从开发服务器加载界面。`dotnet run --project ClipboardSync -- --identity` 会打印配对
使用的稳定 ID、公开密钥和指纹。

## 数据保存位置

|            | Windows                                            | Linux                                              |
|------------|----------------------------------------------------|----------------------------------------------------|
| 设置       | `%APPDATA%\ClipboardSync\settings.json`            | `~/.config/ClipboardSync/settings.json`            |
| 已配对设备 | `%APPDATA%\ClipboardSync\peers.json`               | `~/.config/ClipboardSync/peers.json`               |
| 设备身份   | `%APPDATA%\ClipboardSync\device.key`               | `~/.config/ClipboardSync/device.key`               |
| 历史       | `%LOCALAPPDATA%\ClipboardSync\history.db`          | `~/.local/share/ClipboardSync/history.db`          |
| 图片缓存   | `%LOCALAPPDATA%\ClipboardSync\clipboard`           | `~/.local/share/ClipboardSync/clipboard`           |
| 文件 blob  | `%LOCALAPPDATA%\ClipboardSync\clipboard\blobs`     | `~/.local/share/ClipboardSync/clipboard/blobs`     |
| 文件视图   | `%LOCALAPPDATA%\ClipboardSync\clipboard\items`     | `~/.local/share/ClipboardSync/clipboard/items`     |
| 日志       | `%APPDATA%\ClipboardSync\logs\clipboardsync-*.log` | `~/.config/ClipboardSync/logs/clipboardsync-*.log` |

身份密钥就是配对时对方记录的公钥，因此设置文件夹应只允许你的账户访问。历史记录的上限由 **设置 → 历史 → 保留项目数** 控制。

日志会写入文件，同时镜像到控制台，因此报告问题时提供日志文件就够了。日志每天滚动，保留最近
7 天。

## 注意

- 文件和文件夹会递归复制；一次选择受 **设置 → 同步 → 文件大小上限**（默认 512 MB）和 1,000 个
  条目限制。blob 按内容寻址并在项目间共享；还原的视图会在文件系统允许时使用硬链接。传输是加密
  的，但磁盘上的 blob 存储仍是本地数据文件夹中的普通明文文件。
- **设置 → 同步 → Blob 存储文件夹** 可以更改 blob 的位置。留空则使用应用数据文件夹；更改后会
  移动现有的 blob。
- 设备网络依赖每台设备都能通过 TCP 45456 连接其他设备。UDP 45457 只是发现提示；如果广播被
  阻挡，请使用手动地址栏。
- 关闭窗口会将应用放入托盘。如果希望启动时就隐藏，请打开 **启动时隐藏窗口**；没有托盘的环境会
  始终显示窗口。
- Linux 使用合成器的原生标题栏。Windows 保留窗口边框，并在内部绘制界面标题栏，以避免强制使用
  无边框窗口时 KWin 留下重复标题栏。
- `PhotinoNETUtils` 以 DLL/NPM tarball 的形式从 `Libs/` 引用。更新该库后请重新运行
  `build-and-copy` 脚本；`ClipboardSync` 已经在目标列表中。

## 许可证

MIT — 见 [LICENSE](LICENSE)。
