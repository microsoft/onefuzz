# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

param (
  [string]$mode = "fuzz",
  [string]$restart = "false"
)

Start-Transcript -Path c:\onefuzz-setup.log

$basedir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
try {
  . ("$basedir\onefuzz.ps1")
}
catch {
  Write-Host "Error while loading supporting PowerShell Scripts"
}

function Init-Setup {
  log "onefuzz; start setup"
  $iam = whoami
  log "onefuzz setup run as $iam"

  Enable-LocalUser -Name onefuzz
  mkdir C:\onefuzz -Force
  mv */config.json c:\onefuzz
  mv *-*-*-*/*-*-*-*/*.* c:\onefuzz
  mv *-*-*-*/*.* c:\onefuzz
  mv * c:\onefuzz
  Set-Location C:\onefuzz
  log "onefuzz: moved everything to c:\onefuzz"

  mkdir setup -Force
  mkdir tools -Force
  mkdir instance-specific-setup -Force
  mkdir logs -Force
}

function Install-OnefuzzSetup {
  Set-ExecutionPolicy -ExecutionPolicy unrestricted -Force
  if (Test-Path -Path managed.ps1) {
    log "onefuzz: executing managed-setup"
    ./managed.ps1
  }
  if (Test-Path -Path scaleset-setup.ps1) {
    log "onefuzz: executing scaleset-setup"
    ./scaleset-setup.ps1
  }
  if (Test-Path -Path task-setup.ps1) {
    log "onefuzz: executing task-setup"
    ./task-setup.ps1
  }

  if (Test-Path -Path instance-specific-setup/setup.ps1) {
    log "onefuzz: executing user-setup"
    ./instance-specific-setup/setup.ps1
  } elseif (Test-Path -Path instance-specific-setup/windows/setup.ps1) {
    log "onefuzz: executing user-setup (windows)"
    ./instance-specific-setup/windows/setup.ps1
  }

  if (Test-Path -Path setup/setup.ps1) {
    log "onefuzz: executing user-setup"
    ./setup/setup.ps1
  }
  Optimize-VM
  Install-Debugger
  Install-LLVM
  Enable-SSH
  Install-OnBoot
  Install-VCRedist
  log "onefuzz: setup done"
}

Init-Setup
$config = @{'mode' = $mode; 'restart' = $restart};
Write-OnefuzzConfig($config)
Install-OnefuzzSetup

$config = Get-OnefuzzConfig
if ($config.restart -eq 'true') {
  log "onefuzz: restarting"
  Restart-Computer -Force
}
else {
  log "onefuzz: launching"

  # Task created in `Install-OnBoot`.
  schtasks /run /tn onefuzz
}
