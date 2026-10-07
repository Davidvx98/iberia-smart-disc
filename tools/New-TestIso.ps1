<#
.SYNOPSIS
  Crea una ISO (ISO 9660 + Joliet) a partir de una carpeta, para probar Iberia
  Smart Disc sin grabar discos.

.DESCRIPTION
  Usa IMAPI2, el motor de grabación que trae Windows. Al montar la ISO con doble
  clic, Windows crea un lector de DVD virtual y envía el mismo aviso que con un
  disco real.

  Usa Add-Type para compilar un pequeño ayudante en C# que copia la imagen a
  disco (IMAPI2 la devuelve como IStream). Con AppLocker o en modo de lenguaje
  restringido Add-Type no está permitido: en ese caso usa otro programa para
  crear la ISO (con UDF o Joliet). Todo lo que haya en la carpeta, incluidos los
  archivos ocultos, entra en la ISO.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\New-TestIso.ps1 -Source C:\Pruebas\smart-disc -Output C:\Pruebas\portal2.iso -Label PORTAL2
#>
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$Label = 'IBERIA_DISC',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$sourcePath = (Resolve-Path -LiteralPath $Source).Path
if (-not (Test-Path -LiteralPath (Join-Path $sourcePath 'iberia-disc.json'))) {
    Write-Warning 'La carpeta no tiene iberia-disc.json en la raíz: el programa ignorará este disco.'
}

if (-not ('IberiaIsoWriter' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class IberiaIsoWriter
{
    public static void Write(string path, object comStream)
    {
        var stream = (IStream)comStream;
        var buffer = new byte[2048 * 64];
        IntPtr readPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            using (var file = File.Create(path))
            {
                while (true)
                {
                    stream.Read(buffer, buffer.Length, readPointer);
                    int read = Marshal.ReadInt32(readPointer);
                    if (read <= 0) break;
                    file.Write(buffer, 0, read);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(readPointer);
        }
    }
}
'@
}

$image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$image.FileSystemsToCreate = 3   # ISO 9660 + Joliet: conserva nombres largos como iberia-disc.json
$image.VolumeName = $Label
$image.Root.AddTree($sourcePath, $false)
$result = $image.CreateResultImage()

# Resuelve la ruta respecto a la carpeta actual de PowerShell (no la del proceso).
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if ((Test-Path -LiteralPath $outputPath) -and -not $Force) {
    throw "Ya existe $outputPath. Usa -Force para sobrescribirlo."
}
[IberiaIsoWriter]::Write($outputPath, $result.ImageStream)
Write-Host "ISO creada: $outputPath"
Write-Host 'Haz doble clic en ella para montarla como un lector de DVD.'
