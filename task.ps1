#!/usr/bin/env pwsh

$mod = "$PSScriptRoot/src/ci/tasks.psm1"
Import-Module $mod

$cmdName = $args[0]
if ($null -eq $cmdName) {
    Write-Output "Available Commands"
    Write-Output "------------------"
    (Get-Module $mod -ListAvailable).ExportedCommands.Values | Sort-Object -Property Name |  ForEach-Object {
        Write-Output $_.Name
    }
} else {
    & $cmdName
}
