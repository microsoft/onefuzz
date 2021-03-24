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
// _SancovPcGuardUsed - __sancov$TracePCGuardStart & __sancov$TracePCGuardEnd
// _SancovPcTableUsed - __sancov$PCTableStart & __sancov$PCTableEnd

function findCounterSymbols(exe) {
    var symbols = [
        { name: "MSVC 16.8 bool flag", start: "__sancov$BoolFlagStart", end: "__sancov$BoolFlagEnd" },
        { name: "MSVC 16.8 8bit counters", start: "__sancov$8bitCountersStart", end: "__sancov$8bitCountersEnd" },
        { name: "MSVC pre-16.8", start: "SancovBitmapStart", end: "SancovBitmapEnd" },
        // MSVC compiled libfuzzer targets _also_ include the LLVM symbols, so this needs to be checked after MSVC
        { name: "LLVM 10", start: "__start___sancov_cntrs", end: "__stop___sancov_cntrs" },
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

function dumpCounters(results_dir, should_disable_sympath) {
    if (should_disable_sympath == true) {
        logln(`disabling sympath`);
        execute('.sympath ""');
    } else {
        logln(`not disabling sympath`);
    }

    // Reset to initial break in `ntdll!LdrpDoDebuggerBreak`.
    execute(".restart");

    // Disable FCE from sanitizers.
    execute("sxd av");

    // Run until `LLVMFuzzerTestOneInput()`.
    // This makes us unlikely to have unloaded any modules that the user dynamically loaded,
    // and so we will still be able to dump the coverage tables for those modules.
    execute("bm *!LLVMFuzzerTestOneInput")
    execute("g")

    // run until return from this context
    execute("pt")

    let found = false;
    host.currentProcess.Modules.All(function (module) {
        let result = processModule(module, results_dir);
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
