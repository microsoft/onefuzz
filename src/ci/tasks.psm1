Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$apiServiceDir = Join-Path $PSScriptRoot "../ApiService"

# Powershell does not have the equivalent of 'set -e', so use a helper:
function Invoke-Checked {
    $cmd = $args[0]
    $cmdArgs = @()
    if ($args.Count -gt 1) {
        $cmdArgs = $args[1..($args.Count-1)]
    }
    & $cmd $cmdArgs
    $result = $LASTEXITCODE
    if ($result -ne 0) {
        Write-Error "$cmd $cmdArgs returned failed exit code: $result"
        Exit $result
    }
}

function Get-Version {
    $base_version = Get-Content (Join-Path $PSScriptRoot "../../CURRENT_VERSION")
    $git_hash = Invoke-Checked git rev-parse HEAD

    if ($null -ne $env:GITHUB_REF) {
        if ($env:GITHUB_REF -clike "refs/tags/*") {
            # it is a tag
            return $base_version
        } else {
            # not a tag
            return "$base_version-$git_hash"
        }
    } else {
        & git diff --quiet
        if ($LASTEXITCODE) {
            return "$base_version-$($git_hash)localchanges"
        } else {
            return "$base_version-$git_hash"
        }
    }
}

function Restore-ApiService {
    # install dependencies
    Invoke-Checked sudo npm install -g azurite
    Push-Location $apiServiceDir
    try {
        Invoke-Checked dotnet restore --locked-mode
        Invoke-Checked dotnet tool restore
    }
    finally {
        Pop-Location
    }
}

function Format-ApiService {
    Invoke-Checked dotnet format --verify-no-changes --no-restore $apiServiceDir
}

function Test-ApiService {
    $azurite = & azurite --silent --location /tmp/azurite &
    Push-Location $apiServiceDir
    try {
        Invoke-Checked dotnet test --no-restore --collect:"XPlat Code Coverage" --filter:"Category!=Live"
        if ($null -ne $env:GITHUB_STEP_SUMMARY) {
            Invoke-Checked dotnet tool run reportgenerator "-reports:*/TestResults/*/coverage.cobertura.xml" "-targetdir:coverage" "-reporttypes:MarkdownSummary"
            Get-Content coverage/*.md > $env:GITHUB_STEP_SUMMARY
        }
    }
    finally {
        Pop-Location
        Remove-Job $azurite -Force
    }
}

function Build-ApiService {
    $version = Get-Version
    Write-Output "Building service with version $version"
    Push-Location $apiServiceDir
    try {
        if ($null -ne $env:GITHUB_RUN_ID) {
            # Store GitHub RunID and SHA to be read by the 'info' function
            Tee-Object -InputObject $env:GITHUB_RUN_ID -FilePath 'ApiService/onefuzzlib/build.id'
            Tee-Object -InputObject $env:GITHUB_SHA -FilePath 'ApiService/onefuzzlib/git.version'
        }

        # stamp the build with version
        # note that version might have a suffix of '-{sha}' from get-version.sh
        $prefix, $suffix = $version -split '-'
        Invoke-Checked dotnet build --no-restore --configuration Release /p:VersionPrefix=$prefix /p:VersionSuffix=$suffix
    } 
    finally {
        Pop-Location
    }
}

function Publish-ApiService {
    Push-Location "$PSScriptRoot/.."
    try {
        Get-Version | Out-File "deployment/VERSION"
        Set-Location "ApiService/ApiService"
        Copy-Item 'az-local.settings.json' 'bin/Release/net6.0/local.settings.json'
        Set-Location 'bin/Release/net6.0/'
        $zipFile = 'api-service-net.zip'
        Remove-Item $zipFile -ErrorAction SilentlyContinue
        Invoke-Checked zip -r $zipFile .

        if ($null -ne $env:GITHUB_WORKSPACE) {
            $target = Join-Path $env:GITHUB_WORKSPACE "artifacts/service-net"
            New-Item -ItemType Directory $target
            Copy-Item $zipFile $target
        }
    }
    finally {
        Pop-Location
    }
}

Export-ModuleMember -Function @(
    'Get-Version',

    # ApiService steps
    'Restore-ApiService',
    'Format-ApiService',
    'Test-ApiService',
    'Build-ApiService',
    'Publish-ApiService'
)
