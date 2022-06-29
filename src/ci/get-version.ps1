#!/usr/bin/env pwsh

$base_version = Get-Content (Join-Path $PSScriptRoot "../../CURRENT_VERSION")
$git_hash = & git rev-parse HEAD

if ($null -ne $env:GITHUB_REF) {
    if ($env:GITHUB_REF -clike "refs/tags/*") {
        # it is a tag
        Write-Output $base_version
    } else {
        # not a tag
        Write-Output "$base_version-$git_hash"
    }
} else {
    & git diff --quiet
    if ($LASTEXITCODE) {
        Write-Output "$base_version-$($git_hash)localchanges"
    } else {
        Write-Output "$base_version-$git_hash"
    }
}
