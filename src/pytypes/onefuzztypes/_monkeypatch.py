# TODO: Remove once `smart_union` like support is added to Pydantic
#
# Written by @PrettyWood
# Code from https://github.com/samuelcolvin/pydantic/pull/2092
#
# Original project licensed under the MIT License.

from typing import Any, Dict, Optional, Tuple, Union

from pydantic.error_wrappers import ErrorList
from pydantic.fields import ModelField
from pydantic.types import ModelOrDc
from pydantic.typing import get_origin

ValidateReturn = Tuple[Optional[Any], Optional[ErrorList]]
LocStr = Union[Tuple[Union[int, str], ...], str]


orig = ModelField._validate_singleton


def wrapper(
    self: ModelField,
    v: Any,
    values: Dict[str, Any],
    loc: "LocStr",
    cls: Optional["ModelOrDc"],
) -> ValidateReturn:
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

    return orig(self, v, values, loc, cls)


ModelField._validate_singleton = wrapper  # type: ignore


def _check_hotfix() -> None:
    if ModelField._validate_singleton != wrapper:
        raise Exception("pydantic Union hotfix not applied")
