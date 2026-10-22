param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Push-Location $PSScriptRoot
try {
    & $compiler '/nologo' '/target:winexe' '/optimize+' (('/out:' + (Join-Path $OutputDirectory 'GrayEye.exe'))) '/win32icon:app.ico' '/resource:eye.png,eye.png' '/resource:fields.map,fields.map' '/r:System.Windows.Forms.dll' '/r:System.Drawing.dll' '/r:System.Xml.Linq.dll' '/r:System.Web.Extensions.dll' '/r:System.IO.Compression.dll' '/r:System.IO.Compression.FileSystem.dll' 'Collector.cs' 'TaskEvents.cs' 'Target.cs' 'LayeredBall.cs'
    if ($LASTEXITCODE -ne 0) { throw '灰eye编译失败' }
} finally { Pop-Location }
