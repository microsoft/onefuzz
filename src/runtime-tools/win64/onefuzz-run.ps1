# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param([switch]$docker,
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string] $onefuzzArgs=""
)

$env:RUST_BACKTRACE = "full"

Start-Transcript -Append -Path c:\onefuzz-run.log

$basedir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent

try {
    . ("$basedir\onefuzz.ps1")
}
catch {
    Write-Host "Error while loading supporting PowerShell Scripts"
}

log "onefuzz: starting"


Set-Location C:\onefuzz
if (!$docker){
    Enable-SSH
}
$config = Get-OnefuzzConfig

while ($true) {
    switch ($config.mode) {
        "fuzz" {
            log "onefuzz: fuzzing"

            if ($docker){
                $arglist = "run --config config.json $onefuzzArgs"
                try{
                    Invoke-Expression "c:\onefuzz\tools\win64\onefuzz-agent.exe $arglist"
                } catch {
                    "Error while running onefuzz agent"
                }
            }
            else {
                $arglist = "run --config config.json --redirect-output c:\onefuzz\logs\"
                Start-Process "c:\onefuzz\tools\win64\onefuzz-agent.exe" -ArgumentList $arglist -WindowStyle Hidden -Wait
            }


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

    get-eventlog -logname "application" -message *onefuzz* | format-table -autosize -wrap | out-file c:\onefuzz\logs\onefuzz-eventlog.log

    log "onefuzz unexpectedly exited, restarting after delay"
    Start-Sleep -Seconds 30
}
