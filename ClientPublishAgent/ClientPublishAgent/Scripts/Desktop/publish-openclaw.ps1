function Get-OpenClawExecutable {
	param(
		[Parameter(Mandatory)]
		[string[]]$Names,

		[Parameter(Mandatory)]
		[string]$DisplayName
	)

	$command = Get-Command -Name $Names -CommandType Application -ErrorAction SilentlyContinue |
		Select-Object -First 1
	if ($null -eq $command) {
		throw "未找到 $DisplayName 可执行文件。"
	}

	return $command.Source
}

function Get-OpenClaw7ZipExecutable {
	param(
		[Parameter(Mandatory)]
		[string]$RuntimeIdentifier,
		[switch]$BundledOnly
	)

	$isCurrentWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
	$bundledFileName = switch ($RuntimeIdentifier) {
		"win-x64" { "win-7za.exe"; break }
		"osx-arm64" { "mac-7zz"; break }
		"osx-x64" { "mac-7zz"; break }
		"linux-arm" { "linuxarm-7zz"; break }
		"linux-arm64" { "linuxarm64-7zz"; break }
		"linux-x64" { "linux-x86-x64-7zz"; break }
		default { throw "未提供运行时 $RuntimeIdentifier 对应的 7-Zip 可执行文件。" }
	}
	$repositoryDirectory = if ([string]::IsNullOrWhiteSpace($env:CLIENT_PUBLISH_SOURCE_DIRECTORY)) { Split-Path -Path $PSScriptRoot -Parent } else { Split-Path -Path ([System.IO.Path]::GetFullPath($env:CLIENT_PUBLISH_SOURCE_DIRECTORY)) -Parent }
	$bundledExecutable = Join-Path (Join-Path $repositoryDirectory "Dependency\7z") $bundledFileName
	if (Test-Path -Path $bundledExecutable -PathType Leaf) {
		if (-not $isCurrentWindows) {
			& chmod +x $bundledExecutable
			if ($LASTEXITCODE -ne 0) {
				throw "无法设置 7-Zip 可执行权限：$bundledExecutable"
			}
		}
		return $bundledExecutable
	}

	if ($BundledOnly) { throw "未找到目标运行时 $RuntimeIdentifier 的 7-Zip 可执行文件：$bundledExecutable" }

	$candidates = [System.Collections.Generic.List[string]]::new()
	if (-not [string]::IsNullOrWhiteSpace($env:SEVEN_ZIP_PATH)) {
		$candidates.Add($env:SEVEN_ZIP_PATH)
	}

	$currentDirectory = (Get-Location).Path
	$fileNames = if ($isCurrentWindows) { "7z.exe", "7za.exe", "7z", "7za" } else { "7z", "7za" }
	foreach ($fileName in $fileNames) {
		$candidates.Add((Join-Path $currentDirectory $fileName))
	}

	$command = Get-Command -Name 7z, 7za -CommandType Application -ErrorAction SilentlyContinue |
		Select-Object -First 1
	if ($null -ne $command) {
		$candidates.Add($command.Source)
	}

	if ($isCurrentWindows) {
		foreach ($programFilesDirectory in $env:ProgramFiles, ${env:ProgramFiles(x86)}) {
			if (-not [string]::IsNullOrWhiteSpace($programFilesDirectory)) {
				$candidates.Add((Join-Path $programFilesDirectory "7-Zip\7z.exe"))
			}
		}
	}

	foreach ($candidate in $candidates) {
		if (-not $isCurrentWindows -and $candidate.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
			continue
		}
		if (Test-Path -Path $candidate -PathType Leaf) {
			return $candidate
		}
	}

	throw "未找到适用于当前系统的 7-Zip。请设置 SEVEN_ZIP_PATH，或安装 7-Zip 并将 7z/7za 加入 PATH。"
}

function Download-OpenClawArtifact {
	param(
		[Parameter(Mandatory)]
		[string]$Url,

		[Parameter(Mandatory)]
		[string]$DestinationPath,

		[Parameter(Mandatory)]
		[string]$ApiToken,

		[Parameter(Mandatory)]
		[string]$CurlExecutable
	)

	$partialPath = "$DestinationPath.part"
	Write-Host "开始下载制品：$Url"
	$curlArguments = @(
		"--fail",
		"--location",
		"--retry", "3",
		"--connect-timeout", "30",
		"--max-time", "1800",
		"--silent",
		"--show-error",
		"--header", "X-JFrog-Art-Api: $ApiToken",
		"--output", $partialPath,
		$Url
	)
	& $CurlExecutable @curlArguments
	if ($LASTEXITCODE -ne 0) {
		throw "下载制品失败：$Url"
	}
	Move-Item -Path $partialPath -Destination $DestinationPath -Force
}

function Get-OpenClawApiToken {
	$apiToken = $env:ARTIFACTORY_API_TOKEN
	if ([string]::IsNullOrWhiteSpace($apiToken)) {
		throw "未设置 Artifactory API 令牌。请设置环境变量 ARTIFACTORY_API_TOKEN。"
	}

	return $apiToken
}

function Should-Download-OpenClawArtifact {
	param(
		[Parameter(Mandatory)]
		[string]$ArchivePath,
		[Nullable[bool]]$Redownload = $null
	)

	if (-not (Test-Path -Path $ArchivePath -PathType Leaf)) {
		return $true
	}

	if ($null -ne $Redownload) {
		if (-not $Redownload) {
			Write-Host "使用已有压缩包：$ArchivePath"
		}
		return $Redownload
	}

	do {
		$response = (Read-Host "已存在压缩包：$ArchivePath。是否替换并重新下载？(Y/N)").Trim()
		switch -Regex ($response) {
			'^[Yy]$' { return $true }
			'^[Nn]$' {
				Write-Host "使用已有压缩包：$ArchivePath"
				return $false
			}
			default { Write-Warning "请输入 Y 或 N。" }
		}
	} while ($true)
}

function Install-OpenClawRuntime {
	param(
		[Parameter(Mandatory)]
		[string]$PublishDirectory,

		[Parameter(Mandatory)]
		[string]$AiServiceDirectory,

		[Parameter(Mandatory)]
		[string]$RuntimeIdentifier,
		[Nullable[bool]]$DownloadOpenClaw = $null,
		[Nullable[bool]]$DownloadNodeModules = $null
	)

	$openClawPackageUrl = "https://repo.gildata.com/artifactory/Test_local/ai-data-agent/openclaw/openclaw-release.tgz"
	$nodeModulesPackageUrl = "https://repo.gildata.com/artifactory/Test_local/ai-data-agent/node_modules/node_modules.7z"
	$artifactDirectory = Join-Path (Split-Path -Path $PublishDirectory -Parent) "OpenClaw"
	$downloadDirectory = Join-Path $PublishDirectory ".downloads"
	$openClawArchivePath = Join-Path $artifactDirectory "openclaw-release.tgz"
	$nodeModulesArchivePath = Join-Path $artifactDirectory "node_modules.7z"
	$openClawExtractDirectory = Join-Path $downloadDirectory "openclaw-release"

	New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
	$downloadOpenClawPackage = Should-Download-OpenClawArtifact -ArchivePath $openClawArchivePath -Redownload $DownloadOpenClaw
	$downloadNodeModulesPackage = Should-Download-OpenClawArtifact -ArchivePath $nodeModulesArchivePath -Redownload $DownloadNodeModules
	if ($downloadOpenClawPackage -or $downloadNodeModulesPackage) {
		$apiToken = Get-OpenClawApiToken
		$curlNames = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { "curl.exe", "curl" } else { "curl" }
		$curlExecutable = Get-OpenClawExecutable -Names $curlNames -DisplayName "curl"
		if ($downloadOpenClawPackage) {
			Download-OpenClawArtifact -Url $openClawPackageUrl -DestinationPath $openClawArchivePath -ApiToken $apiToken -CurlExecutable $curlExecutable
		}
		if ($downloadNodeModulesPackage) {
			Download-OpenClawArtifact -Url $nodeModulesPackageUrl -DestinationPath $nodeModulesArchivePath -ApiToken $apiToken -CurlExecutable $curlExecutable
		}
	}
	if ($null -ne $DownloadOpenClaw -and -not [bool]$DownloadOpenClaw) {
		Write-Host "未选择下载 OpenClaw 运行包，跳过解压和 dist 压缩。"
		return
	}

	$tarNames = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) { "tar.exe", "tar" } else { "tar" }
	$tarExecutable = Get-OpenClawExecutable -Names $tarNames -DisplayName "tar"
	if (Test-Path -Path $downloadDirectory) {
		Remove-Item -Path $downloadDirectory -Recurse -Force
	}
	New-Item -ItemType Directory -Path $openClawExtractDirectory -Force | Out-Null
	Write-Host "正在解压 OpenClaw 运行包：$openClawArchivePath"
	& $tarExecutable -xzf $openClawArchivePath -C $openClawExtractDirectory
	if ($LASTEXITCODE -ne 0) {
		throw "解压 OpenClaw 运行包失败：$openClawArchivePath"
	}

	$packageDirectory = Join-Path $openClawExtractDirectory "package"
	$distDirectory = Join-Path $packageDirectory "dist"
	$distArchivePath = Join-Path $packageDirectory "dist.7z"
	if (-not (Test-Path -Path $distDirectory -PathType Container)) {
		throw "OpenClaw 运行包不包含 dist 目录：$distDirectory"
	}
	$distFiles = @(Get-ChildItem -Path $distDirectory -Recurse -File -Force)
	if ($distFiles.Count -eq 0) {
		throw "OpenClaw dist 目录为空，无法生成有效归档：$distDirectory"
	}

	$compressionRuntimeIdentifier = $RuntimeIdentifier
	if ($RuntimeIdentifier.StartsWith("linux-", [System.StringComparison]::Ordinal)) {
		if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
			$compressionRuntimeIdentifier = "win-x64"
		} else {
			$compressionRuntimeIdentifier = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
				"Arm64" { "linux-arm64"; break }
				"X64" { "linux-x64"; break }
				"Arm" { "linux-arm"; break }
				default { throw "当前系统处理器架构不支持 Linux 7-Zip 压缩。" }
			}
		}
	}
	$sevenZipExecutable = Get-OpenClaw7ZipExecutable -RuntimeIdentifier $compressionRuntimeIdentifier
	Write-Host "正在压缩 OpenClaw dist 目录：$distArchivePath"
	$distInputPath = Join-Path $distDirectory "*"
	& $sevenZipExecutable a $distArchivePath $distInputPath -t7z -mx=5 -mmt -y
	if ($LASTEXITCODE -ne 0) {
		throw "压缩 OpenClaw dist 目录失败：$distDirectory"
	}
	if (-not (Test-Path -Path $distArchivePath -PathType Leaf)) {
		throw "压缩 OpenClaw dist 目录后未生成归档：$distArchivePath"
	}
	$distArchive = Get-Item -Path $distArchivePath
	if ($distArchive.Length -le 0) {
		throw "生成的 OpenClaw dist 归档为空：$distArchivePath"
	}
	& $sevenZipExecutable t $distArchivePath -y
	if ($LASTEXITCODE -ne 0) {
		throw "生成的 OpenClaw dist 归档校验失败：$distArchivePath"
	}
	Remove-Item -Path $distDirectory -Recurse -Force

	$workerEntryScript = Join-Path $packageDirectory "wqfaos.mjs"
	if (-not (Test-Path -Path $workerEntryScript -PathType Leaf)) {
		throw "OpenClaw 运行包不包含 Worker 入口脚本：$workerEntryScript"
	}

	if (Test-Path -Path $AiServiceDirectory) {
		Remove-Item -Path $AiServiceDirectory -Recurse -Force
	}
	New-Item -ItemType Directory -Path $AiServiceDirectory -Force | Out-Null
	Get-ChildItem -Path $packageDirectory -Force |
		Move-Item -Destination $AiServiceDirectory -Force
	Copy-Item -Path $nodeModulesArchivePath -Destination (Join-Path $AiServiceDirectory "node_modules.7z") -Force

	$engineDirectory = Split-Path -Path $AiServiceDirectory -Parent
	$sevenZipTargetPath = Join-Path $engineDirectory $(if ($RuntimeIdentifier -eq "win-x64") { "7za.exe" } else { "7zz" })
	$sevenZipRuntimeExecutable = $sevenZipExecutable
	if ($compressionRuntimeIdentifier -ne $RuntimeIdentifier) {
		$sevenZipRuntimeExecutable = Get-OpenClaw7ZipExecutable -RuntimeIdentifier $RuntimeIdentifier -BundledOnly
	}
	Copy-Item -Path $sevenZipRuntimeExecutable -Destination $sevenZipTargetPath -Force
	if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
		& chmod +x $sevenZipTargetPath
		if ($LASTEXITCODE -ne 0) {
			throw "无法设置随 WarrenqAI 发布的 7-Zip 可执行权限：$sevenZipTargetPath"
		}
	}

	Remove-Item -Path $downloadDirectory -Recurse -Force
}
