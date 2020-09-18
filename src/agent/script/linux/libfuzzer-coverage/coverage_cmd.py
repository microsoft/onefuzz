#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import gdb
from typing import List, Tuple, Dict
import os


def get_symbol_addresses(symbol: str) -> List[int]:
    raw = gdb.execute("info variables %s" % symbol, False, True)
    addresses = [int(x.split(" ")[0], 0) for x in raw.split("\n") if x.startswith("0x")]
    return addresses


def get_filename(addr: int) -> str:
    path = gdb.execute("info symbol %d" % addr, False, True).split(" ")[-1].strip()
    return os.path.basename(path)


def get_tables() -> Dict[str, Tuple[int, int]]:
    starts = get_symbol_addresses("__start___sancov_cntrs")
    stops = get_symbol_addresses("__stop___sancov_cntrs")
    if len(starts) != len(stops):
        raise Exception("start and stop sancov cntrs do not match")
    tables = {get_filename(x): (x, y - x) for (x, y) in zip(starts, stops)}
    return tables


class CoverageCommand(gdb.Command):
    def __init__(self):
        super(self.__class__, self).__init__("coverage", gdb.COMMAND_DATA)

    def invoke(self, arg, _):
        argv = gdb.string_to_argv(arg)
        (exe, test_input, result_path) = argv

        gdb.execute("file {}".format(exe))
        gdb.Breakpoint("exit")
        gdb.execute("r {} 2>&1 >/dev/null".format(test_input))

        tables = get_tables()

        for (module, (addr, length)) in tables.items():
            mem = gdb.selected_inferior().read_memory(addr, length)

            with open(os.path.join(result_path, module + ".cov"), "wb") as handle:
                handle.write(mem)


CoverageCommand()
