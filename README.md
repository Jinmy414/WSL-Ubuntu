# WSL-Ubuntu

WSL-Ubuntu 是一个 Windows 上的 WSL 2 桌面控制台。它在名为 `Ubuntu-DS` 的 WSL 发行版中启动独立的 GNOME 会话，确认远程桌面服务就绪后，将 Windows 远程桌面嵌入控制台。控制台支持全屏、自动重连、打开 Ubuntu 终端和关机。

仓库提供控制台源码和初始化脚本。每次安装使用自己的 Ubuntu 用户、RDP 登录和本地生成的 TLS 证书。

## 环境要求

- Windows 11，带 Windows 远程桌面客户端 `mstsc.exe`。
- 支持自定义发行版名称的 WSL 2 和 Ubuntu 26.04 LTS，Ubuntu 默认用户可使用 `sudo`。
- 将仓库克隆到 Windows 本地磁盘，以便 Windows 和 Ubuntu 都能访问。

## 安装

1. 在 Windows 终端更新 WSL 并安装用于本项目的 Ubuntu 实例。按首次启动提示创建 Ubuntu 用户。安装会在该实例内配置默认用户的 GNOME 和 RDP 服务。[Canonical 安装文档](https://ubuntu.com/wsl/docs/latest/howto/install-ubuntu-wsl2/)

   ```powershell
   wsl.exe --update
   wsl.exe --install Ubuntu-26.04 --name Ubuntu-DS
   wsl.exe --list --verbose
   ```

2. 如需显式配置宿主机共享网络，请把 [`setup/wslconfig.example`](setup/wslconfig.example) 中的选项合并到 Windows 用户目录下的 `.wslconfig`。配置作用于该用户的所有 WSL 2 实例。保存工作并关闭所有 WSL 会话后，可执行 `wsl.exe --shutdown`，下一次启动会读取新配置。[Microsoft WSL 配置文档](https://learn.microsoft.com/zh-cn/windows/wsl/wsl-config)

3. 在 PowerShell 切换到仓库根目录，使用发行版的默认 Ubuntu 用户运行安装脚本。脚本会安装 GNOME、生成 TLS 证书并提示创建 RDP 凭据。[GNOME Remote Desktop 配置文档](https://github.com/GNOME/gnome-remote-desktop/blob/main/docs/configuration.md#headless-single-user)

   ```powershell
   wsl.exe -d Ubuntu-DS -- bash ./setup/install-guest-desktop.sh
   ```

   如果脚本提示启用 systemd，在 Ubuntu 的 `/etc/wsl.conf` 中合并以下设置，退出 Linux 会话后执行 `wsl.exe --terminate Ubuntu-DS`，再重试安装。

   ```ini
   [boot]
   systemd=true
   ```

4. 保持 PowerShell 位于仓库根目录，运行 Windows 初始化脚本。它会编译控制台并创建桌面快捷方式和按需运行的保活任务。

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup\install-windows.ps1
   ```

5. 打开桌面上的 **Ubuntu-DS Console**。使用第 3 步创建的 RDP 凭据登录。首次连接时，Windows 可能提示自签名证书。可以在 Ubuntu 中查看本机证书指纹，并与远程桌面窗口的证书详情核对。

   ```bash
   openssl x509 -in ~/.local/share/gnome-remote-desktop/ubuntu-ds.crt -noout -fingerprint -sha1
   ```

## 使用

| 操作 | 入口 |
| --- | --- |
| 开机并连接 | Windows 桌面快捷方式 **Ubuntu-DS Console** |
| 单独打开 Windows 远程桌面 | `bin\ds-desktop.cmd` |
| 打开 Ubuntu 终端 | 控制台工具栏或 `bin\ds.cmd` |
| 用 Windows 当前代理执行 Ubuntu 命令 | `bin\ds.cmd <命令>` 或 `bin\ds-root.cmd <命令>` |
| 诊断连接 | `bin\ds-doctor.cmd` |
| 查看连接日志 | 控制台帮助菜单，或 `%LOCALAPPDATA%\UbuntuDS\connection.log` |
| 备份发行版 | 控制台「备份」，将 Ubuntu-DS 关机并导出到本地 `backups/` |
| 关闭发行版 | 关闭控制台窗口，或运行 `bin\ds-stop.cmd` |
| 本机备份 | 控制台的备份按钮或 `bin\ds-backup.cmd`，关闭发行版后将 VHDX 导出到 `backups/` |

关闭控制台前请保存 Ubuntu 中打开的工作。全屏时可按 Esc 返回窗口。

## 实现范围

- GNOME Shell 以软件渲染运行，远程桌面通过 Windows 本机的 `127.0.0.1:3393` 连接。该配置面向同一台电脑上的 Windows 和 WSL，使用带本地端口转发的网络模式。
- Ubuntu 的代理服务跟随 Windows 当前启用的手动 HTTP/HTTPS 系统代理，同时更新 GNOME 和项目的命令入口。支持范围包括能够读取 GNOME 系统设置或代理环境变量的客户端。已经启动并缓存旧设置的应用可能需要重新打开。
- 剪贴板使用 RDP 客户端和服务端提供的重定向通道，实际行为取决于 Windows 和 GNOME Remote Desktop 的版本。

## 构建控制台

从 Windows 运行 `setup\build-console.cmd`。脚本使用 Windows 自带的 .NET Framework C# 编译器，输出 `bin\UbuntuDS-Console.exe`。启动时通过同目录的 `.rdp` 文件连接本机远程桌面。
