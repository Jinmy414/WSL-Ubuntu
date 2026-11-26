# 连接和网络排查

## 控制台无法进入桌面

运行 `bin\ds-doctor.cmd`。检查输出依次确认 WSL、Ubuntu 路由、代理同步、GNOME 桌面服务、IPv4 中继和 Windows 本机 RDP 握手。安装阶段可以先重跑[安装前检查](installation.md#安装前检查)。

- 需要在 Windows 远程桌面客户端独立连接时，运行 `bin\ds-desktop.cmd`。
- 从控制台帮助菜单打开连接日志，或查看 `%LOCALAPPDATA%\UbuntuDS\connection.log`。
- 若发现其他程序占用 `127.0.0.1:3393`，先关闭或调整对应程序，再重试。Ubuntu 内部的 GNOME Remote Desktop 使用端口 `3391`，由本地 IPv4 中继接入。
- 默认关闭控制台窗口会停止 `Ubuntu-DS`。关窗前请保存 Ubuntu 中的工作；按 Esc 可退出全屏。

## Ubuntu 应用的代理未生效

`ds-proxy-watch` 默认使用 Ubuntu 内的 Clash 混合端口 `7897`，也可以切换到跟随 Windows 的手动 HTTP/HTTPS 系统代理。诊断脚本报告 Windows 代理开关、Ubuntu 所选代理、可达性和 GNOME 代理模式，输出中省略代理地址和凭据。若需验证命令实际使用的路径，从安装目录或 ZIP 解压目录运行以下 PowerShell 命令。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\bin\ds-doctor.ps1 -TestHttps
```

`-TestHttps` 使用项目命令入口的代理逻辑向 `https://example.com/` 发出测试请求，显示 HTTP CONNECT 和目标站点的状态码。`000` 表示未获得目标站点的 HTTP 响应。

通过 `wsl.exe --exec` 直接启动的命令跳过 Bash 的代理初始化。可使用项目命令入口，或显式调用 `/usr/local/bin/ds-proxy-watch --exec <命令>`。缓存了旧设置的应用可能需要重新打开。

### 使用 Ubuntu 内的 Clash

在 Ubuntu 桌面打开 Clash Verge Rev，导入自己的配置并启动代理。使用其「混合端口」，默认示例为 `7897`。保留「系统代理」「TUN 模式」「允许局域网」为关闭状态，由 `ds-proxy-watch` 同步 GNOME 和命令行设置。

使用默认端口时，可直接检查 `--status`。如果 Windows 上也有程序占用同一个端口，Clash 可能自动换端口。把 Ubuntu 内的 Clash 混合端口设为一个独立值，再用下方命令指定同一个值。

```bash
sudo /usr/local/bin/ds-proxy-watch --mode clash 7897
/usr/local/bin/ds-proxy-watch --status
```

`mode=clash` 表示已选择 Ubuntu 本机 Clash。`effective=clash` 表示混合端口通过代理探测。如果 Clash 退出、端口变更或代理不可达，`effective=direct` 表示应用会尝试直接走 WSL 网络路由，Windows 的手动 HTTP/HTTPS 代理不会参与。需要跟随 Windows 手动代理时运行以下命令，已打开的桌面应用可能需要重新打开。

```bash
sudo /usr/local/bin/ds-proxy-watch --mode windows
```

## 提交问题

附上出错步骤、Windows 和 WSL 版本、Ubuntu 版本，以及 `ds-doctor` 的相关结果。先删除日志中的真实用户名、主机路径、局域网地址、代理凭据及桌面截图里的私人内容。


## GPU / CUDA 环境检查

打开控制台「帮助 → GPU / CUDA 环境检查」，点「开始检查」。输入框留空时检查 Ubuntu 的系统 Python。若使用 venv 或 Conda，在已激活的项目终端运行 `python -c "import sys; print(sys.executable)"`，将输出的完整 Linux 路径粘贴到输入框，再点「重新检查」。

| 检查项 | 读法 |
| --- | --- |
| NVIDIA GPU | 列出 Ubuntu 中 nvidia-smi 实际识别的显卡及驱动版本。 |
| CUDA 工具链 | 显示 nvcc 路径和版本。缺少编译器时，下面的 PyTorch 检查仍会执行。 |
| PyTorch GPU 运算 | 显示解释器、PyTorch 版本及其构建使用的 CUDA 版本。在当前 CUDA 设备上计算 1² + 2² + 3²，同步并读回结果 14 后显示「通过」。 |

在解压目录的 Ubuntu 终端中也可执行以下命令。第一条使用当前激活的 Python，第二条适合脚本读取。

```bash
python3 bin/ds-gpu.py
python3 bin/ds-gpu.py --python "/path/to/project/.venv/bin/python" --json
```

全部通过时返回 0，有任何一项未通过时返回 1。检查过程中无需 sudo，也不会安装驱动、CUDA 或 Python 包。
