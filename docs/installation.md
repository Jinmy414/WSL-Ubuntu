# 安装 WSL-Ubuntu

需要 Windows 11、WSL 2、Windows 远程桌面客户端 `mstsc.exe` 和 Ubuntu 26.04 LTS。使用 ZIP 手动安装时，请将 Windows 下载包解压到本地磁盘，在包含 `bin` 和 `setup` 的目录中打开 PowerShell。安装脚本在专用的 `Ubuntu-DS` 发行版中配置 GNOME 桌面和 RDP。默认 Linux 用户须能使用 `sudo`。

在[发布页](https://github.com/Jinmy414/WSL-Ubuntu/releases)下载文件名以 `-windows.zip` 结尾的附件及对应的 `.sha256` 文件，放在同一目录。如需源码，可从 [v1.0.1 版本标签](https://github.com/Jinmy414/WSL-Ubuntu/tree/v1.0.1) 获取。v1.2.1 发布页上的 Source code 归档只有发行文档及仓库配置。解压前，可用 `Get-FileHash -Algorithm SHA256 <ZIP 文件路径>` 计算校验值，并与 `.sha256` 文件第一列核对。

## 安装发行版

```powershell
wsl.exe --update
wsl.exe --install Ubuntu-26.04 --name Ubuntu-DS
wsl.exe --list --verbose
```

首次打开 Ubuntu 时，按提示创建普通用户。WSL 安装与发行版要求见官方说明。 [Canonical 安装文档](https://ubuntu.com/wsl/docs/latest/howto/install-ubuntu-wsl2/)

需要显式配置 Windows 共享网络时，将下载包的 `setup\wslconfig.example` 中的设置合并到 Windows 用户目录的 `.wslconfig`。设置作用于该用户的全部 WSL 2 实例。保存工作并退出所有 WSL 会话后运行 `wsl.exe --shutdown`，再次启动时读取新设置。 [Microsoft WSL 配置文档](https://learn.microsoft.com/zh-cn/windows/wsl/wsl-config)

## 安装前检查

从下载包的解压目录运行以下两个命令。Windows 检查确认 WSL、桌面客户端和本地端口。Ubuntu 检查确认发行版、systemd 用户会话和 RDP 端口。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup\install-windows.ps1 -Check
wsl.exe -d Ubuntu-DS -- bash ./setup/install-guest-desktop.sh --check
```

系统提示启用 systemd 时，在 Ubuntu 的 `/etc/wsl.conf` 中合并以下设置。保存工作并退出 Ubuntu，再从 Windows 运行 `wsl.exe --terminate Ubuntu-DS`，重新执行检查。

```ini
[boot]
systemd=true
```

## 配置 GNOME 和 Windows 控制台

在同一个 PowerShell 窗口依次执行。

```powershell
wsl.exe -d Ubuntu-DS -- bash ./setup/install-guest-desktop.sh
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup\install-windows.ps1
```

Ubuntu 安装程序会生成本地 TLS 证书。初次设置时，请按提示创建 RDP 用户名和密码。重复安装使用已有的 RDP 凭据；若要主动重设，向 Ubuntu 安装命令传入 `--reset-rdp-credentials`。 [GNOME Remote Desktop 的单用户无头会话配置](https://github.com/GNOME/gnome-remote-desktop/blob/main/docs/configuration.md#headless-single-user)

从 v1.1.0 起，Ubuntu 安装程序默认从官方 GitHub 安装 Clash Verge Rev，并在本机验证下载包的 SHA-256。首次使用代理需要在 Clash 里导入自己的配置并选择节点。可加 `--skip-clash` 跳过安装，或者在 Ubuntu 内运行 `bash setup/install-clash.sh` 单独重试。 [Clash Verge Rev 官方发布](https://github.com/clash-verge-rev/clash-verge-rev/releases/tag/v2.5.10)

Windows 安装程序默认使用 ZIP 内的 `bin\UbuntuDS-Console.exe`，创建桌面快捷方式与按需启动的保活任务。以前使用的 `-UsePackagedConsole` 参数仍然有效。首次连接时，打开 **Ubuntu-DS Console**，使用刚才创建的 RDP 凭据登录。遇到自签名证书提示时，可先查看本机证书指纹，再与远程桌面提示中的证书指纹核对。

```bash
openssl x509 -in ~/.local/share/gnome-remote-desktop/ubuntu-ds.crt -noout -fingerprint -sha1
```

## 升级与回退

先读目标版本的 [更新记录](../CHANGELOG.md)，确认修复的问题、配置变化和升级步骤。

- 更新 Windows 控制台时，把新版解压到独立目录，保留旧版目录和下载包。通过新版预检后，再让桌面快捷方式指向新版。回退时可直接打开旧目录中的控制台，或把快捷方式指回旧版。
- 更新 Ubuntu 里的 GNOME、RDP 或代理服务前，先保存工作并用现有控制台导出 `Ubuntu-DS` 的 VHDX 备份。Windows 控制台与 Ubuntu 服务分别安装。若升级同时改动了 Ubuntu，回退时也需要恢复对应的 Ubuntu 备份。
