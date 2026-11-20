# 更新记录

## 未发布

- 默认支持 Ubuntu 内部 Clash Verge Rev，保留 Windows 系统代理模式。
- 安装 GNOME 和 Clash 时优先利用可达的 Windows HTTP/HTTPS 系统代理。
- 连接诊断记录所选代理来源及实际连通状态。
- GNOME 代理设置被桌面程序重置时，代理同步服务会自动纠正。
- 修复全新 WSL 的 WSLg X11 套接字目录为只读时，GNOME 无法启动的问题。

## 1.0.1 首次公开版本

- WPF 控制台支持嵌入 GNOME 桌面、全屏、自动重连和按需保活。
- Windows 和 Ubuntu 安装脚本提供预检，RDP 凭据与 TLS 证书在本机配置。
- HTTP/HTTPS 代理同步覆盖 GNOME、Bash 和项目的 Windows 命令入口。
- 控制台可运行分层连接诊断，检查 Windows 代理、Ubuntu 同步服务与 RDP。
- GitHub 提供构建与连接测试、Windows ZIP 下载包及 SHA-256 校验文件。
