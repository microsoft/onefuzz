param (
    [Parameter()]
    [string] $app_dir="$PSScriptRoot",
    [Parameter(Mandatory=$true)]
    [string] $target
)

try { 
    Push-Location
    Set-Location "$app_dir/../pytypes"
    python setup.py sdist bdist_wheel 
    Copy-Item dist/*.whl "$app_dir/__app__"
    Set-Location $app_dir
    Set-Location __app__
    (New-Guid).Guid > onefuzzlib/build.id
    (Get-Content -path "requirements.txt") -replace 'onefuzztypes==0.0.0', './onefuzztypes-0.0.0-py3-none-any.whl' | Out-File "requirements.txt"
    func azure functionapp publish $target --python
    (Get-Content -path "requirements.txt") -replace './onefuzztypes-0.0.0-py3-none-any.whl', 'onefuzztypes==0.0.0' | Out-File "requirements.txt"
    Remove-Item 'onefuzztypes-0.0.0-py3-none-any.whl'
    Get-Content 'onefuzzlib/build.id'
} 
finally {
    Pop-Location
}