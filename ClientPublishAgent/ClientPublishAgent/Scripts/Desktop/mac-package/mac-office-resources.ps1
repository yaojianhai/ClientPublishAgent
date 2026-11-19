function Move-MacOfficeResources {
	param(
		[Parameter(Mandatory)][string]$MacOsDirectory,
		[Parameter(Mandatory)][string]$ResourcesDirectory
	)

	foreach ($addin in @(Get-ChildItem -LiteralPath $MacOsDirectory -Force | Where-Object { $_.Name -cin @('Addin', 'AddIn') })) {
		$name = $addin.Name
		$addinDirectory = $addin.FullName
		$officeDirectory = Join-Path $addinDirectory 'Office'
		if (-not (Test-Path -LiteralPath $officeDirectory)) { continue }
		$office = Get-Item -LiteralPath $officeDirectory -Force
		if (-not $office.PSIsContainer -or ($office.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
			throw "Office 资源路径不是普通目录：$officeDirectory"
		}
		$resourceAddinDirectory = Join-Path $ResourcesDirectory $name
		New-Item -ItemType Directory -Path $resourceAddinDirectory -Force | Out-Null
		Move-Item -LiteralPath $officeDirectory -Destination (Join-Path $resourceAddinDirectory 'Office')
		& ln -s "../../Resources/$name/Office" $officeDirectory
		if ($LASTEXITCODE -ne 0) { throw "无法创建 Office 资源链接：$officeDirectory" }
		if (-not (Test-Path -LiteralPath $officeDirectory -PathType Container)) { throw "Office 资源链接无效：$officeDirectory" }
	}
}