Linux deb 应用识别与联网策略
===========================

所有生成 deb 的入口共用 package-linux.ps1：
- 直接运行 package-linux.ps1
- linux-upgrade&package.ps1
- linux-publish&upgrade&package.ps1 的 installer / upgrade-installer 分支
升级差分包本身不安装桌面入口；旧版 deb 用户需要先安装一次新版 deb，才能获得本次修复。

Linux 发布脚本按宿主系统执行
----------------------------
- Windows：publish-linux.ps1 将完整 Linux 发布流程转交给 WSL Debian 执行；Windows 负责传递脚本参数和路径。
- Linux：在当前系统原生运行 dotnet、Clang 和 Linux 发布工具，不再启动 WSL。
- linux-publish&upgrade&package.ps1 等组合入口沿用此规则：Windows 发布交给 WSL；后续打包/升级入口也会在 Windows 下各自交给 WSL，Linux 则原生执行。
- WSL Debian 需安装 PowerShell 7、.NET SDK、Clang/LLVM、objcopy、tar；制 deb 还需 dpkg-deb、readelf、find、chmod、ln。
- 源码目录、根目录或输出目录可传 Windows 绝对路径；Windows 分支会转换成 WSL 路径。Linux 分支直接使用 Linux 路径。

本次打包规则
------------
1. 主程序与更新器的桌面入口均指向 /opt/WarrenqF1/warrenqf1。
   /usr/bin/warrenqf1 保留为兼容命令链接。
   统信公开的 deepin-defender 实现通过桌面 Exec 中的 /opt/ 区分第三方应用，
   旧的 /usr/bin 入口可能被归入系统应用。
2. 启动器仍在用户数据目录运行可写副本，保留普通用户自动升级功能。
   启动前明确导出：
	 DEB_PACKAGE_NAME=warrenqf1
	 GIO_LAUNCHED_DESKTOP_FILE=/usr/share/applications/<BundleIdentifier>.desktop
   更新器、AI 启动器与 Node 等子进程正常继承环境时，能够关联到同一个 deb。
   不要绕过启动器直接运行用户目录中的二进制；子进程若自行清空环境，需要在应用侧保留这些变量。
3. 安装/升级配置完成及卸载时，如存在 update-desktop-database，则刷新桌面缓存；
   命令失败会明确报错，工具不存在则输出提示。不会从安装脚本操作用户会话数据库。
4. BundleIdentifier 默认 com.gildata.warrenqf1；自定义时所有安装包入口均传递同一标识。
   DEB_PACKAGE_NAME 始终使用 control 中的真实包名 warrenqf1，而不是 BundleIdentifier。
5. 安装配置时还会为 UID >= 1000 且具有登录 shell 的本地用户复制可执行快捷方式；
   优先采用 xdg-user-dir 返回的桌面路径，并兼容 Desktop、桌面目录；路径缺失时以用户身份创建。
   不覆盖同名用户文件/符号链接；卸载时只清除带有本包标记的快捷方式。
   安装后新建的用户不会自动获得快捷方式，应用仍可从应用菜单启动。

应用菜单与桌面快捷方式是两个位置：/usr/share/applications 只负责应用菜单注册，
不会自动在桌面生成图标。postinst 现在为安装时已有的普通用户建立真正的桌面文件。

是否能自动加入白名单
--------------------
Debian 没有跨发行版通用的“联网白名单”安装字段。
统信公开的 deepin-defender 实现会把新识别的应用初始化为允许联网，同时保留已有设置。
本脚本修复应用发现和进程归属，不关闭防护、不修改全局默认策略、不覆盖管理员的禁止规则。
实际 UOS/麒麟版本或企业策略可能不同，不能仅凭制包成功承诺自动授权。
如果目标系统仍要求授权，需要管理员使用该版本安全中心或厂商支持的集中策略进行授权。

参考实现（用于核对识别行为，不作为所有系统版本的接口承诺）：
https://github.com/linuxdeepin/deepin-defender/blob/9bdab7a741f38ff3d2047b0813a8d08cefd5e226/deepin-defender-server/deepin-defender-datainterface/window/modules/resourcemanager/socketcreatemanager.cpp
相关方法：getPkgNameByPid、getNetControlAppInfos、refreshNetControlStatus。

目标机器验收（保持应用联网策略开启）
------------------------------------
1. 使用系统软件安装器或 sudo apt install 安装新 deb，关闭并重新打开安全中心。
2. 在应用联网策略中查找 Warrenq F1，并检查“系统应用”分类与已有禁止记录。
3. 从启动器或 warrenqf1 命令启动，验证首次下载、更新器下载和 AI/Node 请求。
4. 如需诊断，确认以下文件属于同一包：
	 dpkg-query -S /opt/WarrenqF1/warrenqf1
	 dpkg-query -S /usr/share/applications/com.gildata.warrenqf1.desktop
   自定义 BundleIdentifier 时替换第二条命令中的桌面文件名。
   对实际联网进程 PID 检查 /proc/<PID>/environ 中的两个身份变量。
5. 验证 deb 升级后仍可联网；安全中心已明确禁止的应用应保持禁止。
   若仍无法识别，收集系统版本、安全中心版本与上述结果，确认厂商具体识别/授权接口。

回归检查（PowerShell 7，Linux 或 WSL Debian）
--------------------------------------------
在 ClientPublishAgent 目录运行：
  pwsh -NoProfile -File .\ClientPublishAgent.HarnessChecks\CheckLinuxPackageIdentity.ps1
  pwsh -NoProfile -File .\ClientPublishAgent.HarnessChecks\CheckLinuxInstallerBackup.ps1
身份检查使用隔离临时目录，构造 amd64/arm64 deb 并验证内容、脚本权限、父子进程身份、
启动参数和用户副本升级行为；交叉架构 ELF 仅用于制包测试，不执行，也不安装到当前系统。
需要现有制包工具和 gcc。上述检查不能替代目标 UOS/麒麟安全中心实机验收。

Windows → WSL 日志编码
-----------------------
Windows WSL 启动诊断通过 UTF-16LE 输出，而 Linux 子进程日志为 UTF-8；
linux-common.ps1 会按行分别解码，保持 Linux stdout/stderr 和退出代码，并仅显示一次重复的
localhost 代理提示。该提示是 WSL 网络配置警告，不是打包失败；需要 WSL 访问 Windows 本机代理时，
应另行按机器策略配置 WSL 网络/代理，不要通过关闭应用联网策略处理。
