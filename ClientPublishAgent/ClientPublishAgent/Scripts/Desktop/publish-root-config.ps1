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