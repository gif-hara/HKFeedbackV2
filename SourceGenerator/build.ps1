[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$InstallToUnity,
    [string]$UniCli = 'unicli'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$generatorProject = Join-Path $PSScriptRoot 'HKFeedbackV2.SourceGenerator\HKFeedbackV2.SourceGenerator.csproj'

dotnet build $generatorProject --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Source Generator build failed.' }
if (-not $InstallToUnity) { return }

function Invoke-Unity {
    param([string[]]$UnityArguments)
    $json = & $UniCli @UnityArguments
    if ($LASTEXITCODE -ne 0) { throw "UniCli $($UnityArguments[0]) failed: $($json -join [Environment]::NewLine)" }
    $response = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not $response.success) { throw "UniCli failed: $($response | ConvertTo-Json -Depth 10)" }
    return $response
}

function Wait-UnityReady {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $state = Invoke-Unity @('exec', 'Editor.Status', '--json', '--no-focus', '--timeout', '5000')
            if (-not $state.data.isCompiling -and -not $state.data.isUpdating) { return }
        }
        catch {
            # A script reload can briefly disconnect the editor server.
            if ([DateTime]::UtcNow -ge $deadline) { throw }
        }
        Start-Sleep -Milliseconds 500
    }
    throw 'Unity did not finish importing and reloading scripts within 120 seconds.'
}

Push-Location -LiteralPath $projectRoot
try {
    $status = Invoke-Unity @('check', '--json')
    if (-not $status.data.serverRunning) {
        throw 'Open this project in Unity with the UniCli server installed before installing the generator.'
    }
    Wait-UnityReady

    # Unity creates and maintains all .meta files, including the analyzer label.
    $null = Invoke-Unity @('exec', 'AssetDatabase.Import', '--json', '--no-focus')
    Wait-UnityReady
    $configure = @'
var path = "Assets/HKFeedbackV2/Runtime/HKFeedbackV2.SourceGenerator.dll";
var importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.PluginImporter;
if (importer == null) throw new System.InvalidOperationException("Generator DLL was not imported.");
importer.SetCompatibleWithAnyPlatform(false);
importer.SetCompatibleWithEditor(false);
var asset = UnityEditor.AssetDatabase.LoadMainAssetAtPath(path);
var labels = new System.Collections.Generic.List<string>(UnityEditor.AssetDatabase.GetLabels(asset));
if (!labels.Contains("RoslynAnalyzer")) labels.Add("RoslynAnalyzer");
UnityEditor.AssetDatabase.SetLabels(asset, labels.ToArray());
importer.SaveAndReimport();
return path;
'@
    $null = Invoke-Unity @('eval', $configure, '--json', '--no-focus')
    Wait-UnityReady
    $result = Invoke-Unity @('exec', 'Compile', '--json', '--no-focus', '--timeout', '120000')
    Write-Output ($result | ConvertTo-Json -Depth 10)
}
finally {
    Pop-Location
}
