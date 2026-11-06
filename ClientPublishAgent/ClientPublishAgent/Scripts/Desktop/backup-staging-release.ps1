function Backup-StagingRelease {
	param(
		[Parameter(Mandatory)][string]$StagingDirectory,
		[Parameter(Mandatory)][string]$Version
	)

	if ([string]::IsNullOrWhiteSpace($Version) -or $Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
		throw "备份版本号无效：$Version"
	}
	if (-not (Test-Path -LiteralPath $StagingDirectory -PathType Container)) {
		throw "未找到待备份的暂存发布目录：$StagingDirectory"
	}

	$backupDirectory = Join-Path (Split-Path -Parent $StagingDirectory) "back_$Version"
	if (Test-Path -LiteralPath $backupDirectory) {
		throw "备份目录已存在，未覆盖：$backupDirectory"
	}

	Copy-Item -LiteralPath $StagingDirectory -Destination $backupDirectory -Recurse
	Write-Host "已备份暂存发布目录：$backupDirectory"
}
