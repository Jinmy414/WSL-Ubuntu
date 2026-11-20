# 连接和网络排查

## 控制台无法进入桌面

运行 `bin\ds-doctor.cmd`。检查输出依次确认 WSL、Ubuntu 路由、代理同步、GNOME 桌面服务、IPv4 中继和 Windows 本机 RDP 握手。安装阶段可以先重跑[安装前检查](installation.md#安装前检查)。

- 需要在 Windows 远程桌面客户端独立连接时，运行 `bin\ds-desktop.cmd`。
- 从控制台帮助菜单打开连接日志，或查看 `%LOCALAPPDATA%\UbuntuDS\connection.log`。
- 若发现其他程序占用 `127.0.0.1:3393`，先关闭或调整对应程序，再重试。Ubuntu 内部的 GNOME Remote Desktop 使用端口 `3391`，由本地 IPv4 中继接入。
- 默认关闭控制台窗口会停止 `Ubuntu-DS`。关窗前请保存 Ubuntu 中的工作；按 Esc 可退出全屏。

## Windows 代理已打开，Ubuntu 中某个程序仍无法联网

项目的 `ds-proxy-watch` 服务跟随 Windows 的手动 HTTP/HTTPS 系统代理。诊断脚本报告 Windows 代理开关、Ubuntu 同步服务和 GNOME 代理模式，输出中省略代理地址和凭据。若需验证命令实际使用的路径，从仓库根目录运行以下 PowerShell 命令。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\bin\ds-doctor.ps1 -TestHttps
```

`-TestHttps` 使用项目命令入口相同的代理同步逻辑向 `https://example.com/` 发出测试请求，显示 HTTP CONNECT 和目标站点的状态码。当 Windows 开关已打开、Ubuntu 同步服务正在运行，而请求仍无法完成时，可据此检查目标站点和 Windows 代理的可达性。`000` 表示未获得目标站点的 HTTP 响应。

通过 `wsl.exe --exec` 直接启动的命令跳过 Bash 的代理初始化。可使用项目命令入口，或显式调用 `/usr/local/bin/ds-proxy-watch --exec <命令>`。缓存了旧设置的应用可能需要重新打开。

## 提交问题

附上出错步骤、Windows 和 WSL 版本、Ubuntu 版本，以及 `ds-doctor` 的相关结果。先删除日志中的真实用户名、主机路径、局域网地址、代理凭据及桌面截图里的私人内容。
