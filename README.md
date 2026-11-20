# WSL-Ubuntu

在 Windows 上打开完整的 Ubuntu GNOME 桌面。WSL-Ubuntu 使用 WSL 2 运行独立的 GNOME 会话，并在 WPF 控制台内嵌 Windows 远程桌面。

- **桌面连接**　启动后确认 RDP 握手，再打开桌面。支持断线重连、全屏和本机备份。
- **宿主机代理同步**　GNOME、Bash 和项目命令入口跟随 Windows 当前的手动 HTTP/HTTPS 系统代理。
- **本地安装**　安装者创建自己的 Linux 用户和 RDP 凭据，TLS 证书在本机生成。

## 开始使用

需要 Windows 11、WSL 2 和 Ubuntu 26.04 LTS。可以从仓库的 Releases 下载 Windows ZIP，也可以克隆源码。按文档完成发行版安装和两端预检，再打开桌面上的 **Ubuntu-DS Console**。 [安装指南](docs/installation.md)

| 操作 | 入口 |
| --- | --- |
| 开机并连接 | 桌面快捷方式 **Ubuntu-DS Console** |
| 独立远程桌面 | `bin\ds-desktop.cmd` |
| 打开终端 | 控制台工具栏或 `bin\ds.cmd` |
| 使用 Windows 当前代理执行命令 | `bin\ds.cmd <命令>` 或 `bin\ds-root.cmd <命令>` |
| 诊断连接 | 控制台「帮助 → 运行连接诊断」或 `bin\ds-doctor.cmd` |
| 备份或关机 | 控制台「备份」「关机」 |

构建、打包和本机排查分别见[构建说明](BUILDING.md)与[排查指南](docs/troubleshooting.md)。

## 仓库结构

| 位置 | 内容 |
| --- | --- |
| `bin/` | Windows 启动、诊断和备份入口，及 RDP 模板 |
| `setup/` | WPF 控制台源码、构建脚本和 Windows／Ubuntu 安装程序 |
| `docs/` | 安装、排查和工作原理 |
| `tests/` | 代理和连接行为测试 |
| `.github/` | 构建验证、发布工作流和贡献模板 |

安装引导和文档组织参考了 [Fedora Remix for WSL](https://github.com/WhitewaterFoundry/Fedora-Remix-for-WSL)。查看[工作原理](docs/architecture.md)、[贡献指南](CONTRIBUTING.md)、[更新记录](CHANGELOG.md)或 [MIT 许可证](LICENSE)。

## 联系

- 微信号 `18611567539`
- 邮箱 <1792267909@qq.com>
