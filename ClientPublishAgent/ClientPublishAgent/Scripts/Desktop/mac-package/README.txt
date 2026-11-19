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

手工打包中的签名身份和 notarytool 配置名称会自动保存在本机用户数据目录的
PersonalAgentPlatform/mac-signing-settings.json；未保存过时使用 APPLE_SIGNING_IDENTITY
和 APPLE_NOTARY_PROFILE 环境变量作为初始值。清空输入也会保存。此文件不保存密码、
证书私钥或 notarytool 凭据；证书和凭据仍需事先配置在 Mac 钥匙串中。

OnlyOffice 静态资源在安装包中存放在 Contents/Resources/Addin/Office，
Contents/MacOS/Addin/Office 为指向它的相对符号链接，原运行时路径保持不变。
这样避免 codesign 将 9.4.0-develop 等资源目录误认为嵌套代码包。发布目录及
升级差分包的目录结构不变。DMG 使用 ditto 复制应用以保留符号链接和签名。
