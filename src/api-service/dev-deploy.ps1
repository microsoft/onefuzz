param (
    [Parameter()]
    [string] $app_dir = "$PSScriptRoot",
    # Specifying func app
    [Parameter(Mandatory = $true)]
    [string] $target,
    # Specifying version for function
    [Parameter(Mandatory = $false)]
    [string]
    $version = "0.0.0"
)

try {
    Push-Location
    Set-Location "$app_dir/../pytypes"
    python setup.py sdist bdist_wheel 
    Copy-Item dist/*.whl "$app_dir/__app__"
    Set-Location $app_dir
    Set-Location __app__
    (New-Guid).Guid | Out-File onefuzzlib/build.id -Encoding ascii
    (Get-Content -path "requirements.txt") -replace 'onefuzztypes==0.0.0', './onefuzztypes-0.0.0-py3-none-any.whl' | Out-File "requirements.txt"
    (Get-Content -path "onefuzzlib/__version__.py") -replace '__version__ = "0.0.0"', "__version__ = ""$version""" | Out-File "onefuzzlib/__version__.py" -Encoding utf8
    func azure functionapp publish $target --python
    (Get-Content -path "onefuzzlib/__version__.py") -replace "__version__ = ""$version""", '__version__ = "0.0.0"' | Out-File "onefuzzlib/__version__.py" -Encoding utf8
    (Get-Content -path "requirements.txt") -replace './onefuzztypes-0.0.0-py3-none-any.whl', 'onefuzztypes==0.0.0' | Out-File "requirements.txt"
    Remove-Item 'onefuzztypes-*-py3-none-any.whl'
    Get-Content 'onefuzzlib/build.id'
} 
finally {
    Pop-Location
}
