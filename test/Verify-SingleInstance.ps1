param([string] $PackageName = 'AdaptiveCardWorkbench')

$ErrorActionPreference = 'Stop'
$package = Get-AppxPackage -Name $PackageName
if (@($package).Count -ne 1) {
    throw "Deploy one $PackageName package before running this check."
}

$manifest = $package | Get-AppxPackageManifest
$application = $manifest.Package.Applications.Application
$executable = Join-Path $package.InstallLocation $application.Executable
$appId = "$($package.PackageFamilyName)!$($application.Id)"

function Get-WorkbenchProcess {
    Get-Process | Where-Object { $_.Path -eq $executable }
}

Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$appId" -WindowStyle Hidden
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    Start-Sleep -Milliseconds 200
    $instances = @(Get-WorkbenchProcess)
} until (($instances.Count -eq 1 -and $instances[0].MainWindowHandle -ne 0) -or [DateTime]::UtcNow -ge $deadline)

if ($instances.Count -ne 1 -or $instances[0].MainWindowHandle -eq 0) {
    throw 'Expected one running instance with a main window after launch.'
}

$originalId = $instances[0].Id
1..3 | ForEach-Object {
    Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$appId" -WindowStyle Hidden
    Start-Sleep -Seconds 2
}

$instances = @(Get-WorkbenchProcess)
if ($instances.Count -ne 1 -or $instances[0].Id -ne $originalId -or !$instances[0].Responding) {
    throw 'Repeated launches must leave the original responsive process as the only instance.'
}

"PASS: repeated launches kept one responsive instance (PID $originalId)."
