#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from ..api import Command
from .raw import raw
from .top import Top


class Status(Command):
    """ Monitor status of Onefuzz Instance """

    def raw(self) -> None:
        """ Raw status update stream """
        raw(self.onefuzz, self.logger)

    def top(self, show_details: bool = False) -> None:
        """ Onefuzz Top """
        top = Top(self.onefuzz, self.logger, show_details)
        top.run()
