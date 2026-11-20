# 参与贡献

安装和使用步骤见 [README](README.md)。建议在独立的 WSL 发行版中验证安装、RDP 和代理改动，以便记录首次安装和重复执行的结果。

## 找到改动位置

- `bin/` 是 Windows 用户运行的入口。
- `setup/` 包含控制台源码、构建入口、安装程序和服务定义。
- `.github/` 保存议题与拉取请求模板以及自动验证工作流。

## 验证改动

Windows 控制台可用内置的 .NET Framework C# 编译器构建。在仓库根目录运行：

```powershell
.\setup\build-console.cmd
```

Linux 安装入口和代理脚本可以先做语法检查。在仓库根目录运行：

```bash
for script in setup/*.sh setup/ds-headless-shell setup/ds-proxy-bash; do
  bash -n "$script" || exit
done
python3 -m py_compile setup/ds-proxy-watch
```

GitHub Actions 会编译 Windows 控制台并解析脚本。影响 GNOME、RDP 和代理的改动，还需要在 WSL 2 环境中验证受影响的流程。窗口连接异常时可运行 `bin\ds-doctor.cmd`，提交报告时只附与问题有关的脱敏摘要。

## 提交拉取请求

请说明问题、改动和验证结果。提交公开议题或拉取请求前，删除可识别个人的信息、凭据、证书私钥和未脱敏日志。安全问题请走 [私下报告流程](SECURITY.md)。
