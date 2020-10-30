param (
    [Parameter()]
    [string] $app_dir = "$PSScriptRoot",
    [Parameter()]
    [string] $version = "$null"
)

try { 
    Push-Location

    pip uninstall onefuzztypes -y

    # Get Version and Replace versions
    if ($version -eq "$null") {
        $version = bash .\get-version.sh
    }
    bash .\set-versions.sh $version

    # Create wheel for onefuzztypes
    Set-Location "$app_dir/../pytypes"
    python setup.py sdist bdist_wheel 
    Copy-Item dist/*.whl "$app_dir/../cli"

    # setup creates whl file which replace - to _
    Set-Location "$app_dir/../cli"    
    $_version = $version -replace "-", "_"
    Write-Host $version

    # Replace onefuzztypes requirement to whl file
    (Get-Content -path "requirements.txt") -replace "onefuzztypes==$version", "./onefuzztypes-$_version-py3-none-any.whl" | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    pip install -r .\requirements.txt -r .\requirements-dev.txt

    # Build exe
    pyinstaller onefuzz/__main__.py --onefile --name "onefuzz" --additional-hooks-dir extra/pyinstaller --hidden-import='pkg_resources.py2_warn' --exclude-module tkinter --exclude-module PySide2 --exclude-module PIL.ImageDraw --exclude-module Pillow --clean
    
    # Cleanup
    (Get-Content -path "requirements.txt") -replace "./onefuzztypes-$_version-py3-none-any.whl", "onefuzztypes==$version" | Out-File -FilePath "requirements.txt" -Encoding "ascii"
    Remove-Item "*.whl"
    Set-Location "$app_dir"
    bash .\unset-versions.sh
    
    Write-Host "OneFuzz exe is available at src\cli\dist\onefuzz.exe"
} 
finally {
    Pop-Location
}
