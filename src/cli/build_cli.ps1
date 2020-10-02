param (
    [Parameter()]
    [string] $app_dir = "$PSScriptRoot"
)

try { 
    Push-Location
    Set-Location "$app_dir/../pytypes"
    python setup.py sdist bdist_wheel 
    Copy-Item dist/*.whl "$app_dir"
    Set-Location $app_dir
    (Get-Content -path "requirements.txt") -replace 'onefuzztypes==0.0.0', './onefuzztypes-0.0.0-py3-none-any.whl' | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    pyinstaller $app_dir/onefuzz/__main__.py --onefile --name onefuzz --additional-hooks-dir extra/pyinstaller --hidden-import='pkg_resources.py2_warn' --exclude-module tkinter --exclude-module PySide2 --exclude-module PIL.ImageDraw --exclude-module Pillow --clean
    (Get-Content -path "requirements.txt") -replace './onefuzztypes-0.0.0-py3-none-any.whl', 'onefuzztypes==0.0.0' | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    Remove-Item 'onefuzztypes-0.0.0-py3-none-any.whl'
    Write-Host "exe is available at dist\onefuzz.exe"
} 
finally {
    Pop-Location
}
