function Resolve-DesktopPublishRootDirectory {
	param(
		[Parameter(Mandatory)][ValidateSet("linux-arm64", "linux-x64", "osx-arm64", "osx-x64")][string]$RuntimeIdentifier,
		[Parameter(Mandatory)][string]$RepositoryRoot,
		[string]$RootDirectory
	)

	if (-not [string]::IsNullOrWhiteSpace($RootDirectory)) {
		if ($RootDirectory -eq '/' -or $RootDirectory -match '^[a-zA-Z]:[\\/]$') { return $RootDirectory }
		return $RootDirectory.TrimEnd([char[]]@('/', '\'))
	}

	$configPath = Join-Path $PSScriptRoot "win-package/publish-config.json"
	$config = if (Test-Path -LiteralPath $configPath -PathType Leaf) { Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -ErrorAction Stop } else { $null }
	$baseDirectory = if ($RuntimeIdentifier.StartsWith("linux-")) { [string]$config.LinuxRootDirectory } else { [string]$config.MacOSRootDirectory }
	if ([string]::IsNullOrWhiteSpace($baseDirectory)) { return Join-Path $RepositoryRoot "publish/$RuntimeIdentifier" }

	if ($baseDirectory -ne '/' -and $baseDirectory -notmatch '^[a-zA-Z]:[\\/]$') {
		$baseDirectory = $baseDirectory.TrimEnd([char[]]@('/', '\'))
	}
	if ($baseDirectory -match "(^|[/\\])$([regex]::Escape($RuntimeIdentifier))$") { return $baseDirectory }
	if ($baseDirectory.StartsWith('/')) {
		$trimmedBaseDirectory = $baseDirectory.TrimEnd('/')
		if ([string]::IsNullOrEmpty($trimmedBaseDirectory)) { return "/$RuntimeIdentifier" }
		return "$trimmedBaseDirectory/$RuntimeIdentifier"
	}
	return Join-Path $baseDirectory $RuntimeIdentifier
}

function Resolve-DesktopOssHost {
	param(
		[Parameter(Mandatory)][ValidateSet("win-x64", "linux-arm64", "linux-x64", "osx-arm64", "osx-x64")][string]$RuntimeIdentifier,
		[string]$OssHost
	)
	if (-not [string]::IsNullOrWhiteSpace($OssHost)) { return $OssHost }
	$configPath = Join-Path $PSScriptRoot "win-package/publish-config.json"
	$config = if (Test-Path -LiteralPath $configPath -PathType Leaf) { Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json -ErrorAction Stop } else { $null }
	$baseAddress = if ($null -eq $config -or [string]::IsNullOrWhiteSpace([string]$config.OssHost)) { "https://cloud.gildata.com/gil/upgrade/WarrenQ/WarrenqF1" } else { ([string]$config.OssHost).Trim() }
	$uri = $null
	if ($baseAddress.Length -gt 2048 -or $baseAddress -match '[\x00-\x1f\x7f]' -or
		-not [Uri]::TryCreate($baseAddress, [UriKind]::Absolute, [ref]$uri) -or
		$uri.Scheme -notin @('http', 'https') -or [string]::IsNullOrEmpty($uri.Host) -or
		-not [string]::IsNullOrEmpty($uri.UserInfo) -or -not [string]::IsNullOrEmpty($uri.Query) -or -not [string]::IsNullOrEmpty($uri.Fragment)) {
		throw "升级基础地址无效，请在打包配置中填写不包含账号、查询参数或片段的 HTTP/HTTPS 地址。"
	}
	if ($uri.AbsolutePath.TrimEnd('/').Split('/')[-1] -cin @('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')) {
		throw "升级基础地址不能包含平台和架构后缀。"
	}
	return $baseAddress.TrimEnd('/') + "/" + $RuntimeIdentifier
}
