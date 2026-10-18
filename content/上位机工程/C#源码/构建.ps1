param(
    [switch]$Publish,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '汽车上位机.csproj'
$arguments = @('build', $project, '-c', 'Release', '--nologo')
if ($Publish) {
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\C#可运行文件'))
    }
    $arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x86',
        '--self-contained', 'true', '--nologo', '-o', $OutputDirectory)
}
elseif (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $arguments += @('-o', $OutputDirectory)
}
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "构建失败，dotnet 退出码为 $LASTEXITCODE" }
