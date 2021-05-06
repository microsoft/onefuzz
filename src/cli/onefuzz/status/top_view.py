#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

from datetime import datetime
from typing import Any, List, Optional, Sequence, Tuple, Union

from asciimatics.event import KeyboardEvent
from asciimatics.exceptions import ResizeScreenError, StopApplication
from asciimatics.scene import Scene
from asciimatics.screen import Screen
from asciimatics.widgets import (
    Divider,
    Frame,
    Label,
    Layout,
    MultiColumnListBox,
    TextBox,
    Widget,
)

from .cache import TopCache, fmt


def now() -> datetime:
    return datetime.now()


# border + title + quit msg
BASE_LINES = 5

# divider + name + header
EXTRA_LINES_PER_WIDGET = 3


def column_config(fields: Optional[List[str]]) -> List[Union[int, str]]:
    base: List[Union[int, str]] = []
    if fields:
        base += [1] * (len(fields) - 1)
    else:
        base += [1]

    base += ["100%"]
    return base


class TopView(Frame):
    def __init__(self, screen: Any, cache: TopCache, show_details: bool):

        super(TopView, self).__init__(
            screen, screen.height, screen.width, has_border=True, can_scroll=False
        )
        self.cache = cache
        self.show_details = show_details
        self.set_theme("monochrome")
        self.palette["title"] = (
            Screen.COLOUR_BLACK,
            Screen.A_NORMAL,
            Screen.COLOUR_WHITE,
        )

        self.job_count = len(self.cache.jobs)
        self.pool_count = len(self.cache.pools)

        max_widget_height = (
            int((screen.height - BASE_LINES) / 3) - EXTRA_LINES_PER_WIDGET
        )

        layout = Layout([1], fill_frame=True)
        self.add_layout(layout)

        self.onefuzz_reversed = {
            "pools": False,
            "jobs": True,
            "tasks": True,
            "messages": True,
        }

        dimensions = {
            "pools": {
                "height": min(self.pool_count + 1, max_widget_height),
                "setup": self.cache.POOL_FIELDS,
            },
            "jobs": {
                "height": min(self.job_count + 1, max_widget_height),
                "setup": self.cache.JOB_FIELDS,
            },
            "tasks": {"height": Widget.FILL_FRAME, "setup": self.cache.TASK_FIELDS},
            "messages": {
                "height": min(10, max_widget_height),
                "setup": ["Updated", "Type", "Message"],
            },
            "status": {"height": 1},
        }

        for name in ["status", "pools", "jobs", "tasks", "messages"]:
            if name == "messages" and not self.show_details:
                continue

            titles = dimensions[name].get("setup")

            if titles:
                title = TextBox(1, as_string=True, name=name + "_title")
                title.disabled = True
                title.custom_colour = "label"
                layout.add_widget(title)

            widget = MultiColumnListBox(
                dimensions[name]["height"],
                column_config(titles),
                [],
                titles=titles,
                name=name,
                add_scroll_bar=bool(titles),
            )
            if not titles:
                widget.disabled = True

            layout.add_widget(widget)
            layout.add_widget(Divider())

        layout.add_widget(Label("Press `q` to quit or `r` to reorder."))
        self.fix()

    def process_event(self, event: Any) -> Any:
        result = super(TopView, self).process_event(event)
        if isinstance(event, KeyboardEvent):
            # quit
            if event.key_code in [ord("q"), Screen.ctrl("c")]:
                raise StopApplication("")

            # toggle sort for current widget
            if event.key_code in [ord("r")]:
                if self.focussed_widget.name in self.onefuzz_reversed:
                    self.onefuzz_reversed[
                        self.focussed_widget.name
                    ] = not self.onefuzz_reversed[self.focussed_widget.name]

        return result

    def to_display(self, data: Sequence, reverse: bool) -> List[Tuple[str, int]]:
        data = sorted(data, reverse=reverse)
        data = [fmt(x) for x in data]
        return [(x, y) for (y, x) in enumerate(data)]

    def auto_resize(self, name: str) -> None:
        """recompute widget width based on max length of all of the values"""
        widget = self.find_widget(name)
        for column in range(len(widget._columns) - 1):

            sizes = [len(x[0][column]) + 1 for x in widget.options]
            if widget._titles:
                sizes.append(len(widget._titles[column]) + 1)
            widget._columns[column] = max(sizes)

    def render_base(self, name: str, data: Any) -> None:
        reverse = self.onefuzz_reversed.get(name, False)
        self.find_widget(name).options = self.to_display(data, reverse)
        self.auto_resize(name)
        title = self.find_widget(name + "_title")
        if title:
            title.value = "%s: %s" % (name.title(), fmt(len(data)))

    def update(self, frame_no: int) -> Any:
        if len(self.cache.pools) != self.pool_count:
            raise ResizeScreenError("resizing because of a differing pool count")
        if len(self.cache.jobs) != self.job_count:
            raise ResizeScreenError("resizing because of a differing job count")
        self.render_base("status", [[now(), "| " + self.cache.endpoint]])
        self.render_base("pools", self.cache.render_pools())
        self.render_base("jobs", self.cache.render_jobs())
        self.render_base("tasks", self.cache.render_tasks())

        if self.show_details:
            self.render_base("messages", self.cache.messages)

        super(TopView, self).update(frame_no)

    @property
    def frame_update_count(self) -> int:
        return 25


def render(data: TopCache, show_details: bool) -> None:
    while True:
        try:
            Screen.wrapper(
                lambda screen, data_ref, show_details: screen.play(
                    [Scene([TopView(screen, data_ref, show_details)], -1)],
                    stop_on_resize=True,
                ),
                catch_interrupt=True,
                arguments=[data, show_details],
            )
            return
        except ResizeScreenError:
            pass
