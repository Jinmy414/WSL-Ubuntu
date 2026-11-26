# 使用 Ubuntu-DS 控制台

双击 Windows 桌面的 **Ubuntu-DS Console**。控制台启动 Ubuntu-DS 后会连接 GNOME 桌面。窗口底部显示连接状态、CPU、内存和运行时间。

| 操作 | 入口 |
| --- | --- |
| 打开 Ubuntu 终端 | 控制台工具栏的「终端」，或运行安装目录中的 `bin\ds.cmd` |
| 在 Windows 端按当前代理运行 Linux 命令 | `bin\ds.cmd <命令>`。需要 root 权限时使用 `bin\ds-root.cmd <命令>` |
| 在独立的远程桌面窗口中连接 | `bin\ds-desktop.cmd` |
| 查看连接卡在哪一步 | 「帮助 → 运行连接诊断」，或运行 `bin\ds-doctor.cmd` |
| 检查 GPU、CUDA 和 PyTorch | 「帮助 → GPU / CUDA 环境检查」，可指定项目使用的 Ubuntu Python 路径 |
| 备份发行版 | 保存工作后，选择「虚拟机 → 备份」 |
| 关闭 Ubuntu | 保存工作后，选择「虚拟机 → 关机」。默认情况下，关闭控制台窗口也会停止 Ubuntu-DS |

[连接和网络排查](troubleshooting.md) · [返回项目首页](../README.md)
