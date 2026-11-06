function Copy-OfficeDependency {
	param(
		[Parameter(Mandatory)]
		[string]$RepositoryDirectory,

		[Parameter(Mandatory)]
		[string]$PublishDirectory
	)

	$officeSourceDirectory = Join-Path $RepositoryDirectory "Dependency/Office"
	$officeTargetDirectory = Join-Path $PublishDirectory "Addin/Office"
	if (-not (Test-Path -LiteralPath $officeSourceDirectory -PathType Container)) {
		throw "未找到 Office 依赖目录：$officeSourceDirectory"
	}

	New-Item -ItemType Directory -Path $officeTargetDirectory -Force | Out-Null
	Get-ChildItem -LiteralPath $officeSourceDirectory -Force | Copy-Item -Destination $officeTargetDirectory -Recurse -Force
	Write-Host "Office 依赖复制完成：$officeTargetDirectory"
}
