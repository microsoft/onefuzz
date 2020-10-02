param (
    [Parameter()]
    [string] $app_dir = "$PSScriptRoot",
    [Parameter()]
    [string] $version = "0.0.0"
)

try { 
    Push-Location
    Set-Location "$app_dir/../pytypes"
    python setup.py sdist bdist_wheel 
    Copy-Item dist/*.whl "$app_dir/../cli"
    Set-Location "$app_dir/../cli"
    (Get-Content -path "requirements.txt") -replace "onefuzztypes==0.0.0", "./onefuzztypes-0.0.0-py3-none-any.whl" | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    pyinstaller onefuzz/__main__.py --onefile --name "onefuzz-$version" --additional-hooks-dir extra/pyinstaller --hidden-import='pkg_resources.py2_warn' --exclude-module tkinter --exclude-module PySide2 --exclude-module PIL.ImageDraw --exclude-module Pillow --clean
    (Get-Content -path "requirements.txt") -replace "./onefuzztypes-0.0.0-py3-none-any.whl", "onefuzztypes==0.0.0" | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    Remove-Item "onefuzztypes-0.0.0-py3-none-any.whl"
    Write-Host "OneFuzz exe is available at dist\onefuzz-$version.exe"
} 
finally {
    Pop-Location
}
