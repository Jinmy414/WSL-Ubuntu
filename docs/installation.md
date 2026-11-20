# 安装 WSL-Ubuntu

需要 Windows 11、WSL 2、Windows 远程桌面客户端 `mstsc.exe` 和 Ubuntu 26.04 LTS。请将 Windows 下载包解压或将源码克隆到 Windows 本地磁盘，并在该目录打开 PowerShell。安装脚本在专用的 `Ubuntu-DS` 发行版中配置 GNOME 桌面和 RDP。默认 Linux 用户须能使用 `sudo`。

从 GitHub Releases 下载时，将 ZIP 和 `.sha256` 文件放在同一目录。解压前，可用 `Get-FileHash -Algorithm SHA256 <ZIP 文件路径>` 计算校验值，并与 `.sha256` 文件第一列核对。

## 安装发行版

```powershell
wsl.exe --update
wsl.exe --install Ubuntu-26.04 --name Ubuntu-DS
wsl.exe --list --verbose
```

首次打开 Ubuntu 时，按提示创建普通用户。WSL 安装与发行版要求见官方说明。 [Canonical 安装文档](https://ubuntu.com/wsl/docs/latest/howto/install-ubuntu-wsl2/)

需要显式配置 Windows 共享网络时，将 [`.wslconfig` 示例](../setup/wslconfig.example) 中的设置合并到 Windows 用户目录的 `.wslconfig`。设置作用于该用户的全部 WSL 2 实例。保存工作并退出所有 WSL 会话后运行 `wsl.exe --shutdown`，再次启动时读取新设置。 [Microsoft WSL 配置文档](https://learn.microsoft.com/zh-cn/windows/wsl/wsl-config)

## 安装前检查

从项目根目录运行以下两个命令。Windows 检查确认 WSL、桌面客户端和本地端口。Ubuntu 检查确认发行版、systemd 用户会话和 RDP 端口。使用 GitHub Windows 下载包时，在第一个命令末尾加 `-UsePackagedConsole`。

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

当前 `main` 源码默认从官方 GitHub 安装 Clash Verge Rev，并在本机验证下载包的 SHA-256。首次使用代理需要在 Clash 里导入自己的配置并选择节点。可加 `--skip-clash` 跳过安装，或者在 Ubuntu 内运行 `bash setup/install-clash.sh` 单独重试。 [Clash Verge Rev 官方发布](https://github.com/clash-verge-rev/clash-verge-rev/releases/tag/v2.5.10)

Windows 安装程序会编译控制台，并创建桌面快捷方式与按需启动的保活任务。使用 GitHub Windows 下载包时，在第二个命令末尾加 `-UsePackagedConsole`，直接使用包内程序。首次连接时，打开 **Ubuntu-DS Console**，使用刚才创建的 RDP 凭据登录。遇到自签名证书提示时，可先查看本机证书指纹，再与远程桌面提示中的证书指纹核对。

```bash
openssl x509 -in ~/.local/share/gnome-remote-desktop/ubuntu-ds.crt -noout -fingerprint -sha1
```
