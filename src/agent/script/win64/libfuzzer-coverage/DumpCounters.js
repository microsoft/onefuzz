function logln(msg) {
    host.diagnostics.debugLog(`[+] ${msg}\n`);
}

function execute(cmd) {
    return host.namespace.Debugger.Utility.Control.ExecuteCommand(cmd);
}

function findSymbolAddr(mod, sym) {
    return host.getModuleSymbolAddress(mod, sym);
}

// Read a memory region and return a `DataModel.Models.Array`.
//
// https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/native-objects-in-javascript-extensions-debugger-objects
function readU8Array(addr, len) {
    let u8Size = 1;
    let isSigned = false;
    return host.memory.readMemoryValues(addr, len, u8Size, isSigned);
}

// For future research: Other tables of interest in MSVC 16.8
// _Sancov8bitUsed - __sancov$8bitCountersStart & __sancov$8bitCountersEnd
// _SancovPcGuardUsed - __sancov$TracePCGuardStart & __sancov$TracePCGuardEnd
// _SancovPcTableUsed - __sancov$PCTableStart & __sancov$PCTableEnd

function findCounterSymbols(exe) {
    var symbols = [
        { name: "LLVM 10", start: "__start___sancov_cntrs", end: "__stop___sancov_cntrs" },
        { name: "MSVC 16.8", start: "__sancov$BoolFlagStart", end: "__sancov$BoolFlagEnd" },
        { name: "MSVC pre-16.8", start: "SancovBitmapStart", end: "SancovBitmapEnd" },
    ];

    for (let entry of symbols) {
        let start = findSymbolAddr(exe, entry.start);
        let end = findSymbolAddr(exe, entry.end);
        if (start && end) {
            logln(`using ${entry.name} symbols - ${start}:${end}`);
            return { start, end };
        }
    }
    return null;
}


function findCounterTable(exe) {
    // Assume current process name is the module name.
    let offsets = findCounterSymbols(exe);
    if (offsets == null) {
        return null;
    }

    // `Int64` values from the debugger data model, not lossy 53-bit JavaScript floats.
    let { start, end } = offsets;

    // Use `Int64.subtract` to maintain precision.
    const length = end.subtract(start);

    // Counter data as a `DataModel.Models.Array`.
    const data = readU8Array(start, length);

    return { start, end, length, data };
}

function writeCounters(data, covFilename) {
    const CreateFile = host.namespace.Debugger.Utility.FileSystem.CreateFile;

    logln("writing to file " + covFilename);
    const f = CreateFile(covFilename, "CreateAlways");
    f.WriteBytes(data);
    f.Close();
}

function processModule(module, results_dir) {
    logln(`processing ${module.Name}`);
    let table = findCounterTable(module);
    if (table == null) {
        logln(`no tables  ${module.Name}`);
        return false;
    }
    let filename = module.Name.split("\\").slice(-1)[0];
    writeCounters(table.data, results_dir + '\\' + filename + ".cov");
    return true;
}

function dumpCounters(results_dir, sample_name) {
    // Reset to initial break in `ntdll!LdrpDoDebuggerBreak`.
    execute(".restart");

    // Disable FCE from sanitizers.
    execute("sxd av");

    // Run to exit break in `ntdll!NtTerminateProcess`.
    execute("g");

    let found = false;
    host.currentProcess.Modules.All(function (module) {
        let result = processModule(module, results_dir, sample_name);
        if (result) {
            found = true;
        }
        return true;
    });

    if (!found) {
        throw new Error("unable to find sancov counter symbols");
    }
}

function initializeScript() {
    return [
        // Define a new function alias. After `.scriptload`, this can be invoked as
        // `!dumpcounters <cov-file>`, which will write a binary dump of the 8-bit PC
        // counter table to `cov-file`.
        //
        // https://docs.microsoft.com/en-us/windows-hardware/drivers/debugger/debugger-data-model-function-aliases
        new host.functionAlias(dumpCounters, 'dumpcounters'),
    ];
}
