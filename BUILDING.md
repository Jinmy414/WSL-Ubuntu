# 构建和打包

在 Windows 11 的 PowerShell 中打开仓库根目录。只编译 Windows 控制台时，使用 Windows 自带的 64 位 .NET Framework C# 编译器。

```powershell
.\setup\build-console.cmd
```

默认输出 `bin\UbuntuDS-Console.exe`，也可以把其他输出路径作为参数传给构建脚本。

## 生成 Windows 下载包

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\setup\package-windows.ps1 -Version 1.0.1 -OutputDirectory .\dist
```

打包脚本会重新编译控制台，并生成 `WSL-Ubuntu-1.0.1-windows.zip` 和对应的 `.zip.sha256`。包内包含控制台、RDP 模板和安装脚本。Windows 和 Ubuntu 的安装步骤见[安装指南](docs/installation.md)。

## 发布验收

GitHub Actions 运行 Windows 编译、脚本语法和连接相关测试。修改桌面服务或 RDP 流程后，还应在单独的 WSL 2 Ubuntu 实例内检查首次登录、重复安装、断线重连和代理开关切换。 [贡献指南](CONTRIBUTING.md)
