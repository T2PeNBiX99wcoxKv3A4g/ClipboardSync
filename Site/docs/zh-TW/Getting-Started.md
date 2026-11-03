# 入門

ClipboardSync 讓已配對裝置的剪貼簿保持同步。在一台裝置上複製文字、連結或圖片，其他裝置也會收到。所有通訊都由裝置之間透過加密工作階段直接完成。

## 需要什麼

- Windows 或 Linux，x64。建置是自包含的，不需要安裝 .NET 執行階段。
- 兩台能互相連線的裝置。TCP 45456 用於配對和剪貼簿通訊，UDP 45457 只用於區域網路探索。
- Linux 需要 WebKitGTK 4.1 和 AppIndicator 函式庫。沒有 AppIndicator 時應用程式仍可執行，只是沒有系統匣圖示。

## 啟動

執行 `ClipboardSync.exe`（或 Linux 二進位檔）。關閉視窗後應用程式會留在系統匣。同一時間只執行一個執行個體，再次啟動會喚回既有視窗。

## 配對第一台裝置

開啟 **裝置 → 配對裝置**。選擇探索到的裝置，或輸入位址，然後比對兩邊畫面顯示的配對碼。參見 [配對](Pairing)。

## 複製內容

在任意應用程式中複製文字、URL 或圖片。項目會出現在這台裝置下方，傳送到已配對裝置，並套用到它們的剪貼簿。共享內容和離線時的行為參見 [同步](Syncing)。

## 資料位置

| 內容 | Windows | Linux |
| --- | --- | --- |
| 設定、身分、已配對裝置 | `%APPDATA%\ClipboardSync` | `~/.config/ClipboardSync` |
| 歷史與圖片快取 | `%LOCALAPPDATA%\ClipboardSync` | `~/.local/share/ClipboardSync` |
| 日誌 | `%APPDATA%\ClipboardSync\logs` | `~/.config/ClipboardSync/logs` |

身分金鑰就是配對時對方記錄的公鑰，因此該資料夾應只允許你的帳戶存取。

## 接下來

- [配對](Pairing) — 探索、位址和配對碼。
- [同步](Syncing) — 看板、傳輸狀態和重試。
- [安全性](Security) — 加密涵蓋的範圍。
