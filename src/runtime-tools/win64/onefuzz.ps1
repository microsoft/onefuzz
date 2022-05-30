# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

$env:Path += ";C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\;C:\onefuzz\win64;C:\onefuzz\tools\win64;C:\onefuzz\tools\win64\radamsa;$env:ProgramFiles\LLVM\bin"
$env:ONEFUZZ_ROOT = "C:\onefuzz"
$env:ONEFUZZ_TOOLS = "C:\onefuzz\tools"
$env:LLVM_SYMBOLIZER_PATH = "llvm-symbolizer"
$env:RUST_LOG = "info"

$logFile = "C:\onefuzz.log"
function log ($message) {
  $timestamp = [DateTime]::Now.ToString("yyyy-MM-dd HH:mm:ss")
  "$timestamp $message" | Add-Content $logFile
  Write-Host -ForegroundColor Yellow $timestamp $message @args
}

function Setup-Silent-Notification {
   # https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/registry-entries-for-silent-process-exit
   log "installing registry key for silent termination notification of onefuzz-agent"
   reg import c:\onefuzz\tools\win64\onefuzz-silent-exit.reg
   log "done importing registry key"
}

function Uninstall-OneDrive {
  Stop-Process -Name OneDrive -Force -ErrorAction Ignore
  Stop-Process -Name FileCoAuth -Force -ErrorAction Ignore
  Unregister-ScheduledTask -TaskName *OneDrive* -Confirm:$false

  if (Test-Path $env:windir\SysWOW64\OneDriveSetup.exe) {
     log "uninstalling onedrive from syswow64"
     Start-Process -FilePath $env:windir\SysWOW64\OneDriveSetup.exe -ArgumentList /uninstall
  }

  if (Test-Path $env:windir\System32\OneDriveSetup.exe) {
     log "uninstalling onedrive from system32"
     Start-Process -FilePath $env:windir\System32\OneDriveSetup.exe -ArgumentList /uninstall
  }
}

function Optimize-VM {
  log "adding exclusion path to windows defender"
  Add-MpPreference -ExclusionPath "c:\Onefuzz"

  log "uninstalling OneDrive"
  Uninstall-OneDrive

  log "uninstalling Skype"
  Get-AppxPackage Microsoft.SkypeApp | Remove-AppxPackage

  log "uninstalling Cortana"
  # https://docs.microsoft.com/en-us/windows/configuration/cortana-at-work/cortana-at-work-policy-settings
  Get-AppxPackage Microsoft.549981C3F5F10 | Remove-AppxPackage

  log "disable Windows Search"
  sc.exe stop "WSearch"
  sc.exe config "WSearch" start= disabled
}

function Set-SetSSHACL {
  icacls.exe "$env:ProgramData\ssh\ssh_host_dsa_key" /remove "NT AUTHORITY/Authenticated Users"
  icacls.exe "$env:ProgramData\ssh\ssh_host_dsa_key" /inheritance:r
  Get-Acl "$env:ProgramData\ssh\ssh_host_dsa_key" | Set-Acl "$env:ProgramData\ssh\administrators_authorized_keys"

  icacls.exe "c:\programdata\ssh\ssh_host_dsa_key" /remove "NT AUTHORITY/Authenticated Users"
  icacls.exe "c:\programdata\ssh\ssh_host_dsa_key" /inheritance:r
  Get-Acl "c:\programdata\ssh\ssh_host_dsa_key" | Set-Acl "c:\programdata\ssh\administrators_authorized_keys"
}

function Enable-SSH {
  log "enabling ssh"

  Get-Service -name sshd
  if ($?) {
    log "ssh already installed"
  }
  else {
    Remove-WindowsCapability -Online -Name OpenSSH.*
    Get-WindowsCapability -Online -Name OpenSSH.* | Add-WindowsCapability -Online
    log "enabling ssh: adding ssh"
  }

  Start-Service sshd
  Set-Service -Name sshd -StartupType 'Automatic'
  Enable-SSHTrafic

  Get-Service -Name sshd
  if ($?) {
    log "onefuzz: ssh running"
  }
  else {
    log "onefuzz: specifying onefuzz must reboot before starting"
    Set-Restart
  }

  Set-SetSSHACL

  log "enabling ssh: done"
}

function Enable-SSHTrafic {
  log "enabling ssh firewall"
  $name = "sshd"
  $display = "OpenSSH Server (sshd)"
  $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
  if ($rule) {
    log "Firewall rule '$name' already created"
    Enable-NetFirewallRule -Name $name
  }
  else {
    New-NetFirewallRule -Name $name -DisplayName $display -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
  }
  log "enabling ssh firewall done"
}

function Install-OnBoot {
  log "adding onboot: starting"
  schtasks /create /sc onstart /tn onefuzz /tr "powershell.exe -ExecutionPolicy Unrestricted -File c:\onefuzz\tools\win64\onefuzz-run.ps1 -WindowStyle Hidden" /ru SYSTEM
  log "adding onboot: done"
}

function Install-LLVM {
  log "installing llvm"
  $ProgressPreference = 'SilentlyContinue'
  $exe_path = "llvm-setup.exe"
  Invoke-WebRequest -uri https://github.com/llvm/llvm-project/releases/download/llvmorg-10.0.0/LLVM-10.0.0-win64.exe -OutFile $exe_path
  cmd /c start /wait $exe_path /S
  $env:Path += ";$env:ProgramFiles\LLVM\bin"
  log "installing llvm: done"
}

function Install-Debugger {
  log "installing debugger"

  # Windows PowerShell reports progress on every byte downloaded causing performance issues.
  $ProgressPreference = 'SilentlyContinue'
  Invoke-WebRequest -Uri 'https://download.microsoft.com/download/4/2/2/42245968-6A79-4DA7-A5FB-08C0AD0AE661/windowssdk/winsdksetup.exe' -OutFile debug-setup.exe
  Start-Process "./debug-setup.exe" -ArgumentList "/norestart /quiet /features OptionId.WindowsDesktopDebuggers /log dbg-install.log" -Wait

  # New-NetFirewallRule -Name onefuzzdebug -DisplayName 'CDB listener' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 1337
  netsh advfirewall firewall add rule name="cdb in" dir=in action=allow program="C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe" enable=yes
  # netsh advfirewall firewall add rule name="cdb out" dir=out action=allow program="C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe" enable=yes

  log "installing debugger: done"
}

function Write-OnefuzzConfig($config) {
  $config | ConvertTo-Json | Out-File "C:\onefuzz\onefuzz-config.json"
}

function Get-OnefuzzConfig {
  return (Get-Content "C:\onefuzz\onefuzz-config.json"  | ConvertFrom-Json)
}

function Set-Restart {
  $config = Get-OnefuzzConfig
  $config.restart = 'true'
  Write-OnefuzzConfig($config)
}

function Install-VCRedist {
  log "installing VC Redist"
  $x64Release = 'https://aka.ms/vs/15/release/VC_redist.x64.exe'
  $x86Release = 'https://aka.ms/vs/15/release/VC_redist.x86.exe'
  $ProgressPreference = 'SilentlyContinue'
  Invoke-WebRequest -Uri $x64Release -OutFile "C:\onefuzz\vcredist_x64.exe"
  Invoke-WebRequest -Uri $x86Release -OutFile "C:\onefuzz\vcredist_x86.exe"
  Start-Process -FilePath C:\onefuzz\vcredist_x64.exe -ArgumentList "/install /q /norestart" -Wait -WindowStyle Hidden
  Start-Process -FilePath C:\onefuzz\vcredist_x86.exe -ArgumentList "/install /q /norestart" -Wait -WindowStyle Hidden
  log "installing VC Redist: done"
}
