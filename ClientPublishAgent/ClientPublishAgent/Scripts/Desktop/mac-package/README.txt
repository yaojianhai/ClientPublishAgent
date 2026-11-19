macOS 升级差分工具
==================

- x64/GeneralUpdate.Tools.Cmd：Intel Mac（x86_64）。
- arm64/GeneralUpdate.Tools.Cmd：Apple Silicon 原生环境（arm64）。

工具随仓库提供，构建和发布时会复制到输出目录的 Scripts/Desktop/mac-package。
升级脚本根据 uname -m 选择可在当前环境执行的工具，而不是根据目标安装包的架构选择。
只有目标版本高于当前版本且 OldRelease 中已有上一版文件时，才需要执行差分工具。

如果旧的 Rider 构建报“未找到 macOS 差分工具”，请同步包含两个工具文件的提交，
重新构建 ClientPublishAgent.Desktop，并确认 Debug 或发布输出中存在上述两个文件。
不要用 Windows 或 Linux 版本的差分工具替代。
