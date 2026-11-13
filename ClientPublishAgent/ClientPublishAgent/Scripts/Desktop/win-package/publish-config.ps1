function Get-WindowsPublishRootDirectory {
	$configPath = Join-Path $PSScriptRoot "publish-config.json"
	if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
		throw "未找到 Windows 发布配置文件：$configPath"
	}

	$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -ErrorAction Stop
	$rootDirectory = [string]$config.RootDirectory
	if ([string]::IsNullOrWhiteSpace($rootDirectory)) {
		throw "Windows 发布配置未设置 RootDirectory：$configPath"
	}
	$isAbsoluteWindowsPath = $rootDirectory -match '^[a-zA-Z]:[\\/]' -or $rootDirectory -match '^\\\\'
	if (-not $isAbsoluteWindowsPath) {
		throw "Windows 发布 RootDirectory 必须是绝对路径：$rootDirectory"
	}

	if ($rootDirectory -match '^[a-zA-Z]:[\\/]$' -or $rootDirectory -match '^\\\\[^\\]+\\[^\\]+[\\/]?$') { return $rootDirectory.TrimEnd('/') }
	return $rootDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
}

function Invoke-WindowsBatchWithLegacyEncoding {
	param([Parameter(Mandatory)][string]$CommandLine)

	$originalOutputEncoding = [Console]::OutputEncoding
	try {
		[Console]::OutputEncoding = [System.Text.Encoding]::GetEncoding(936)
		$commandOutput = @(& $env:ComSpec /d /c $CommandLine 2>&1)
		$exitCode = $LASTEXITCODE
	} finally {
		[Console]::OutputEncoding = $originalOutputEncoding
	}
	foreach ($line in $commandOutput) { Write-Host $line }
	return $exitCode
}
