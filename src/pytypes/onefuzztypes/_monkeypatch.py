# TODO: Remove once `smart_union` like support is added to Pydantic
#
# Written by @PrettyWood
# Code from https://github.com/samuelcolvin/pydantic/pull/2092
#
# Original project licensed under the MIT License.

from typing import TYPE_CHECKING, Any, Dict, Optional, Union

from pydantic.fields import ModelField
from pydantic.typing import get_origin

if TYPE_CHECKING:
    from pydantic.fields import LocStr, ValidateReturn
    from pydantic.types import ModelOrDc

upstream_validate_singleton = ModelField._validate_singleton


# this is a direct port of the functionality from the PR discussed above, though
# *all* unions are considered "smart" for our purposes.
def wrap_validate_singleton(
    self: ModelField,
    v: Any,
    values: Dict[str, Any],
    loc: "LocStr",
    cls: Optional["ModelOrDc"],
) -> "ValidateReturn":
    if self.sub_fields:
        if get_origin(self.type_) is Union:
            for field in self.sub_fields:
                if v.__class__ is field.outer_type_:
                    return v, None
            for field in self.sub_fields:
                try:
                    if isinstance(v, field.outer_type_):
                        return v, None
                except TypeError:
                    pass

    return upstream_validate_singleton(self, v, values, loc, cls)


ModelField._validate_singleton = wrap_validate_singleton  # type: ignore


# this should be included in any file that defines a pydantic model that uses a
# Union and calls to it should be removed when Pydantic's smart union support
# lands
def _check_hotfix() -> None:
    if ModelField._validate_singleton != wrap_validate_singleton:
        raise Exception("pydantic Union hotfix not applied")
