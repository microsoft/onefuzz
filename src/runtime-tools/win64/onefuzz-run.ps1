# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

Start-Transcript -Path c:\onefuzz-run.log

$basedir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent

try {
    . ("$basedir\onefuzz.ps1")
}
catch {
    Write-Host "Error while loading supporting PowerShell Scripts"
}

log "onefuzz: starting"

Set-Location C:\onefuzz
Enable-SSH
$config = Get-OnefuzzConfig

while ($true) {
    switch ($config.mode) {
        "fuzz" {
            log "onefuzz: fuzzing"
            Start-Process "c:\onefuzz\tools\win64\onefuzz-supervisor.exe" -ArgumentList "run --config config.json" -WindowStyle Hidden -Wait
        }
        "repro" {
            log "onefuzz: starting repro"
            Start-Process "powershell.exe" -ArgumentList "-ExecutionPolicy Unrestricted -File repro.ps1" -WindowStyle Hidden -Wait
        }
        default {
            log "invalid mode"
            exit 1
        }
    }
    log "onefuzz unexpectedly exited, restarting after delay"
    Start-Sleep -Seconds 30
}
