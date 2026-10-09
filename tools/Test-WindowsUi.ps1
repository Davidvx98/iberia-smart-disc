# Prueba las ventanas del .exe en Windows con .NET Framework (Windows PowerShell).
# No instala el programa ni cambia el inicio con Windows.
param([Parameter(Mandatory = $true)][string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
$assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $Executable).Path)
$flags = [System.Reflection.BindingFlags]'Public,Static'
$info = $assembly.GetType('IberiaSmartDisc.AppInfo', $true)
$version = $info.GetProperty('VersionText', $flags).GetValue($null, $null)
$display = $info.GetProperty('DisplayVersion', $flags).GetValue($null, $null)
$expected = [xml](Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props'))
if ($version -ne $expected.SelectSingleNode('/Project/PropertyGroup/ReleaseVersion').InnerText) {
    throw "La versión del ejecutable no coincide: $version"
}
if ($display -notlike '*BETA*') { throw 'Falta BETA en la versión visible' }

function Get-ControlText($control) {
    $control.Text
    foreach ($child in $control.Controls) { Get-ControlText $child }
}

$logType = $assembly.GetType('IberiaSmartDisc.Logging.AppLog', $true)
$log = [Activator]::CreateInstance($logType, [object[]]@('logs', $false))
$modeType = $assembly.GetType('IberiaSmartDisc.UI.InstallMode', $true)
$formType = $assembly.GetType('IberiaSmartDisc.UI.InstallForm', $true)
foreach ($modeName in @('Install', 'Update')) {
    $mode = [Enum]::Parse($modeType, $modeName)
    $form = [Activator]::CreateInstance($formType, [object[]]@($mode, [Version]'0.0.0', $log))
    try {
        $form.Show()
        [System.Windows.Forms.Application]::DoEvents()
        if (-not $form.Visible -or (Get-ControlText $form) -notcontains "Versión $display") {
            throw "La ventana $modeName no muestra la versión BETA"
        }
        Write-Host "$modeName visible con $display"
    } finally { $form.Dispose() }
}

# Arranque residente en modo portátil para comprobar también la ventana principal.
$paths = $assembly.GetType('IberiaSmartDisc.AppPaths', $true)
$paths.GetMethod('UsePortable', $flags).Invoke($null, @()) | Out-Null
$commandType = $assembly.GetType('IberiaSmartDisc.CommandLine', $true)
$command = $commandType.GetMethod('Parse', $flags).Invoke($null, [object[]]@(,[string[]]@('--portable', '--background')))
$stateType = $assembly.GetType('IberiaSmartDisc.Hosting.AppState', $true)
$state = $stateType.GetMethod('Load', $flags).Invoke($null, [object[]]@($log))
$state.Settings.TrayIcon = $false
$residentType = $assembly.GetType('IberiaSmartDisc.Hosting.ResidentApp', $true)
$resident = [Activator]::CreateInstance($residentType, [object[]]@($state, $command))
try {
    $resident.ShowMainWindow()
    [System.Windows.Forms.Application]::DoEvents()
    $main = @([System.Windows.Forms.Application]::OpenForms | Where-Object { $_.GetType().Name -eq 'MainForm' })[0]
    if (-not $main -or -not $main.Visible -or (Get-ControlText $main) -notcontains "v$display") {
        throw 'La ventana principal no muestra la versión BETA'
    }
    Write-Host "Ventana principal visible con $display"
} finally { $resident.Dispose() }
