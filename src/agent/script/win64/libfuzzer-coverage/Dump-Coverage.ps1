Param(
    [Parameter(Mandatory=$true, Position=0)]
    [String]
    $Exe,

    [Parameter(Mandatory=$true, Position=1)]
    [String]
    $TestInput,

    [String]
    $OutDir,

    [String]
    $Cdb = 'cdb.exe'
)

$testInputFile = (Get-Item $TestInput).Name
$covFile = "${OutDir}/${testInputFile}.cov".Replace('\', '/')

$cdbCmd = (
    ".scriptload ${PSScriptRoot}\DumpCounters.js",

    # `\` required to escape double quotes for CDB.
    # Needed to pass coverage filename as a string.
    "!dumpcounters \""${covFile}\""",

    'q'
) -join '; '

& $Cdb -c $cdbCmd $Exe $testInput
