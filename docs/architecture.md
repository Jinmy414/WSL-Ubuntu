# 工作原理

## Windows 桌面连接

`UbuntuDS-Console.exe` 在后台请求 Ubuntu 启动 GNOME 用户服务。Windows 向 `127.0.0.1:3393` 发送 RDP 握手请求，通过后才启动 `mstsc.exe` 并嵌入远程桌面窗口。客户端断开时，控制台可以重新连接同一个 Linux 桌面会话。用户主动关机和关闭控制台会停止 `Ubuntu-DS`。

## Ubuntu 桌面服务

`ds-shell-headless.service` 启动独立的 GNOME Wayland 会话。WSLg 的共享 X11 套接字目录为只读时，GNOME 会以纯 Wayland 模式启动。`gnome-remote-desktop-headless.service` 等待 GNOME 的远程桌面接口就绪，并在 Ubuntu 内部监听 RDP。`ds-rdp-v4.service` 只监听 Ubuntu 本机的 IPv4 回环 `3393`，转发到 IPv6 回环 `3391`。由 WSL 的本地转发将该入口提供给 Windows。

安装时在 Ubuntu 的用户目录生成 TLS 证书。安装者在本机录入 RDP 凭据，Windows 远程桌面负责证书提示和登录。

## 代理同步

`ds-proxy-watch` 定期检查所选 HTTP/HTTPS 代理并探测 HTTPS CONNECT，然后更新 GNOME 与用户服务的环境。默认使用 Ubuntu 内部 Clash。Windows 模式读取宿主机的手动系统代理。Clash 模式会确认混合端口由 Linux 本地监听，防止将同端口的 Windows 代理认成 Ubuntu 内部 Clash。Bash 在显示新提示符时载入最新设置，通过 `bin\ds.cmd` 或 `bin\ds-root.cmd` 启动的单条命令也使用同一代理来源。

Windows 能通过 RDP 连接 Ubuntu 和 Ubuntu 应用能通过代理访问互联网是两条独立的链路。连接诊断会分别报告这两条链路的状态。 [连接和网络排查](troubleshooting.md)

## 本地数据

桌面、发行版和配置由安装者在本机创建。`backups/` 保存本机导出的 VHDX。连接日志默认写入 `%LOCALAPPDATA%\UbuntuDS\connection.log`。
