#!/usr/bin/env python
#
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

"""
Simple CLI builder on top of a defined API (used for OneFuzz)
"""

import argparse
import inspect
import json
import logging
import os
import sys
import traceback
from enum import Enum
from typing import (
    Any,
    Callable,
    Dict,
    List,
    Optional,
    Sequence,
    Tuple,
    Type,
    TypeVar,
    Union,
)
from uuid import UUID

import jmespath
from docstring_parser import parse as parse_docstring
from msrest.serialization import Model
from onefuzztypes.primitives import Container, Directory, File, PoolName, Region
from pydantic import BaseModel, ValidationError

LOGGER = logging.getLogger("cli")

JMES_HELP = (
    "JMESPath query string. See http://jmespath.org/ "
    "for more information and examples."
)


def call_setup(api: Any, args: argparse.Namespace) -> None:
    setup = getattr(api, "__setup__", None)
    if setup is None:
        return

    myargs = {}
    for arg in inspect.getfullargspec(setup).args[1:]:
        if hasattr(args, arg):
            myargs[arg] = getattr(args, arg)

    for arg in inspect.getfullargspec(setup).kwonlyargs:
        if hasattr(args, arg):
            myargs[arg] = getattr(args, arg)

    setup(**myargs)


def call_func(func: Callable, args: argparse.Namespace) -> Any:
    myargs = {}
    for arg in inspect.getfullargspec(func).args[1:]:
        if hasattr(args, arg):
            myargs[arg] = getattr(args, arg)

    for arg in inspect.getfullargspec(func).kwonlyargs:
        if hasattr(args, arg):
            myargs[arg] = getattr(args, arg)
    return func(**myargs)


def arg_bool(arg: str) -> bool:
    acceptable = ["true", "false"]
    if arg not in acceptable:
        raise argparse.ArgumentTypeError(
            "invalid value: %s, must be %s"
            % (
                repr(arg),
                " or ".join(acceptable),
            )
        )
    return arg == "true"


def arg_dir(arg: str) -> str:
    if not os.path.isdir(arg):
        raise argparse.ArgumentTypeError("not a directory: %s" % arg)
    return arg


def arg_file(arg: str) -> str:
    if not os.path.isfile(arg):
        raise argparse.ArgumentTypeError("not a file: %s" % arg)
    return arg


def is_optional(annotation: Any) -> bool:
    return is_a(annotation, Union, count=2) and (
        get_arg(annotation, 1) == type(None)  # noqa: E721
    )


def is_a(annotation: Any, origin: Any, count: Optional[int] = None) -> bool:
    return (
        (
            isinstance(origin, tuple)
            and getattr(annotation, "__origin__", None) in origin
        )
        or (
            not inspect.isclass(origin)
            and getattr(annotation, "__origin__", None) == origin
        )
    ) and (count is None or len(annotation.__args__) == count)


def is_dict(annotation: Any) -> bool:
    return is_a(annotation, dict, count=1)


def get_arg(annotation: Any, index: Optional[int] = None) -> Union[Any, List[Any]]:
    if index is None:
        return annotation.__args__
    else:
        return annotation.__args__[index]


def add_base(parser: argparse.ArgumentParser) -> None:
    parser.add_argument(
        "-v", "--verbose", action="count", help="increase output verbosity", default=0
    )
    parser.add_argument(
        "--format", choices=["json", "raw"], default="json", help="output format"
    )
    parser.add_argument("--query", help=JMES_HELP)


def enum_help(entry: Type[Enum]) -> str:
    return "accepted %s: %s" % (entry.__name__, ", ".join([x.name for x in entry]))


def tuple_help(entry: Any) -> str:
    doc = []
    for item in entry:
        if inspect.isclass(item) and issubclass(item, Enum):
            doc.append(
                "accepted %s: %s." % (item.__name__, ", ".join([x.name for x in item]))
            )
    return " ".join(doc)


class Builder:
    def __init__(self, api_types: List[Any]):
        self.type_parsers: Dict[Any, Dict[str, Any]] = {
            str: {"type": str},
            int: {"type": int},
            UUID: {"type": UUID},
            Container: {"type": str},
            Region: {"type": str},
            PoolName: {"type": str},
            File: {"type": arg_file},
            Directory: {"type": arg_dir},
        }
        self.api_types = tuple(api_types)
        self.top_level = argparse.ArgumentParser(add_help=False)
        add_base(self.top_level)

        self.main_parser = argparse.ArgumentParser()
        add_base(self.main_parser)

    def add_version(self, version: str) -> None:
        self.main_parser.add_argument(
            "--version",
            action="version",
            version="%(prog)s {version}".format(version=version),
        )

    def parse_api(self, api: Any) -> None:
        setup = getattr(api, "__setup__", None)
        if setup:
            self.parse_function(setup, self.main_parser)
        self.parse_nested_instances(self.main_parser, api)

    def get_help(self, obj: Any) -> str:
        return (parse_docstring(obj.__doc__).short_description or "").strip()

    def parse_function(self, func: Callable, parser: argparse.ArgumentParser) -> None:
        sig = inspect.signature(func)

        arg_docs = {}
        docs = parse_docstring(func.__doc__)
        for opt in docs.params:
            if opt.description:
                arg_docs[opt.arg_name] = opt.description
        for arg in sig.parameters:
            if arg == "self":
                continue
            help_doc = arg_docs.get(arg)
            args, kwargs = self.parse_param(arg, sig.parameters[arg], help_doc=help_doc)
            parser.add_argument(*args, **kwargs)

    def parse_param(
        self, name: str, param: inspect.Parameter, help_doc: Optional[str] = None
    ) -> Tuple[List[str], Dict[str, Any]]:
        """Parse a single parameter"""

        default = param.default
        annotation = param.annotation
        kwargs = self.parse_annotation(name, annotation, default, help_doc=help_doc)
        if not (
            isinstance(default, bool) or default in [None, inspect.Parameter.empty]
        ):
            if "help" in kwargs:
                kwargs["help"] += " (default: %(default)s)"
            else:
                kwargs["help"] = "(default: %(default)s)"
            kwargs["default"] = default

        optional = False

        if default is not inspect.Parameter.empty:
            optional = True

        if "optional" in kwargs:
            optional = True
            del kwargs["optional"]

        args = ["--" + name if optional else name]
        return args, kwargs

    def parse_annotation_class(
        self, name: str, annotation: Any, default: Any
    ) -> Optional[Dict[str, Any]]:
        if issubclass(annotation, Enum):
            result = {
                "type": annotation,
                "help": enum_help(annotation),
                "metavar": annotation.__name__,
            }
            return result

        if issubclass(annotation, bool):
            if default is False:
                return {
                    "action": "store_true",
                    "optional": True,
                    "help": "(Default: False.  Sets value to True)",
                }
            elif default is True:
                return {
                    "action": "store_false",
                    "optional": True,
                    "help": "(Default: True.  Sets value to False)",
                }
            elif default is None:
                return {
                    "type": arg_bool,
                    "optional": True,
                    "help": "Provide 'true' to set to true and 'false' to set to false",
                }
            else:
                raise Exception("Argument parsing error: %s", repr(default))

        if issubclass(annotation, BaseModel):

            def parse_model(data: str) -> object:
                if data.startswith("@"):
                    try:
                        with open(data[1:], "r") as handle:
                            data = handle.read()
                    except FileNotFoundError:
                        raise argparse.ArgumentTypeError("not a file: %s" % data[1:])

                try:
                    return annotation.parse_raw(data)
                except ValidationError as err:
                    raise argparse.ArgumentTypeError("parsing error\n" + str(err))

            parse_model.__name__ == annotation.__name__

            result = {
                "metavar": annotation.__name__,
                "help": "JSON for %s.  use @file to read from a file"
                % annotation.__name__,
                "type": parse_model,
            }
            return result

        return None

    def build_dict_parser(self, annotation: Any) -> Dict[str, Any]:
        (key_arg, val_arg) = get_arg(annotation)

        class AsDictCustom(argparse.Action):
            def __call__(
                self,
                _parser: argparse.ArgumentParser,  # noqa: F841 - unused args required by argparse
                namespace: argparse.Namespace,
                values: Union[str, Sequence[Any], None],
                option_string: str = None,  # noqa: F841 - unused args required by argparse
            ) -> None:
                if values is None:
                    return

                for arg in values:
                    if "=" not in arg:
                        raise argparse.ArgumentTypeError(
                            "unable to parse value as a key=value pair: %s" % repr(arg)
                        )

                as_dict: Dict[str, str] = {
                    key_arg(k): val_arg(v) for k, v in (x.split("=", 1) for x in values)
                }
                setattr(namespace, self.dest, as_dict)

        metavar = "%s=%s" % (key_arg.__name__, val_arg.__name__)
        return {"action": AsDictCustom, "nargs": "+", "metavar": metavar}

    def parse_annotation(
        self,
        name: str,
        annotation: Any,
        default: Any,
        help_doc: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Parse a single type annotation and get a signature appropriate
        for argparse.add_argument
        """

        result: Dict[str, Any] = {}
        if help_doc:
            result["help"] = help_doc

        if annotation in self.type_parsers:
            result.update(self.type_parsers[annotation].copy())
            return result

        if is_optional(annotation):
            result.update(self.parse_annotation(name, get_arg(annotation, 0), default))
            result["optional"] = True
            return result

        if is_a(annotation, (list, List), count=1):
            result.update(self.parse_annotation(name, get_arg(annotation, 0), default))
            result["nargs"] = "*"
            return result

        if is_a(annotation, (dict, Dict)):
            result.update(self.build_dict_parser(annotation))
            return result

        if is_a(annotation, (tuple, Tuple)):
            types = get_arg(annotation)

            def parse_tuple(data: str) -> Tuple[Any, ...]:
                split = data.split("=", len(types))
                if len(split) != len(types):
                    raise ValueError("invalid length: %s" % data)
                return tuple([x(y) for (x, y) in zip(types, split)])

            parse_tuple.__name__ = "=".join([x.__name__ for x in types])
            result.update(
                {
                    "metavar": parse_tuple.__name__,
                    "help": tuple_help(types),
                    "type": parse_tuple,
                }
            )
            return result

        if inspect.isclass(annotation):
            class_result = self.parse_annotation_class(name, annotation, default)
            if class_result is not None:
                result.update(class_result)
                if help_doc and result["help"] != help_doc:
                    result["help"] = "%s %s" % (help_doc, result["help"])
                return result

        # isinstance type signatures doesn't support TypeVar
        if hasattr(annotation, "__class__") and annotation.__class__ == TypeVar:

            def parse_typevar(data: str) -> object:
                for possible in annotation.__constraints__:
                    try:
                        return possible(data)
                    except ValueError:
                        pass
                raise argparse.ArgumentTypeError("Error parsing: %s" % data)

            result.update(
                {
                    "metavar": name,
                    "help": annotation.__name__,
                    "type": parse_typevar,
                }
            )
            return result

        raise Exception("unsupported annotation: %s - %s" % (name, annotation))

    def get_children(
        self, inst: Callable, is_callable: bool = False, is_typed: bool = False
    ) -> List[Tuple[str, Callable]]:
        entries = []
        for name in dir(inst):
            if name.startswith("_"):
                continue

            func = getattr(inst, name)
            if is_callable and not callable(func):
                continue

            if is_typed and not isinstance(func, self.api_types):
                continue

            entries.append((name, func))

        return entries

    def parse_instance(
        self, inst: Callable, subparser: argparse._SubParsersAction
    ) -> None:
        """Expose every non-private callable in a class instance"""
        for (name, func) in self.get_children(inst, is_callable=True):
            sub = subparser.add_parser(name, help=self.get_help(func))
            add_base(sub)
            self.parse_function(func, sub)
            sub.set_defaults(func=func)

    def parse_nested_instances(
        self, main_parser: argparse.ArgumentParser, inst: Callable, level: int = 0
    ) -> None:
        subparser = main_parser.add_subparsers(
            title="subcommands", dest="level_%d" % level
        )

        for (name, endpoint) in self.get_children(inst, is_typed=True):
            parser = subparser.add_parser(
                name, help=self.get_help(endpoint), parents=[self.top_level]
            )

            method_subparser = parser.add_subparsers(
                title="subcommands", dest="level_%d" % (level + 1)
            )

            for (nested_name, nested_endpoint) in self.get_children(
                endpoint, is_typed=True
            ):
                nested = method_subparser.add_parser(
                    nested_name,
                    help=self.get_help(nested_endpoint),
                    parents=[self.top_level],
                )
                self.parse_nested_instances(
                    nested,
                    nested_endpoint,
                    level=level + 2,
                )

            self.parse_instance(endpoint, method_subparser)

        self.parse_instance(inst, subparser)

    def parse_args(self) -> argparse.Namespace:
        return self.main_parser.parse_args()

    def print_nested_help(self, args: argparse.Namespace) -> None:
        level = 0
        parser = self.main_parser
        while True:
            if parser._subparsers is None:
                break
            if parser._subparsers._actions is None:
                break
            choices = parser._subparsers._actions[-1].choices
            value = getattr(args, "level_%d" % level)
            if value is None:
                parser.print_help()
                return

            if not choices:
                break
            if isinstance(choices, dict):
                parser = choices[value]
            else:
                return
            level += 1


def output(result: Any, output_format: str, expression: Optional[Any]) -> None:
    if isinstance(result, bytes):
        sys.stdout.buffer.write(result)
    else:
        if isinstance(result, list) and result and isinstance(result[0], BaseModel):
            # cycling through json resolves all of the nested BaseModel objects
            result = [json.loads(x.json(exclude_none=True)) for x in result]
        if isinstance(result, BaseModel):
            # cycling through json resolves all of the nested BaseModel objects
            result = json.loads(result.json(exclude_none=True))
        if isinstance(result, Model):
            result = result.as_dict()
        if expression is not None:
            result = expression.search(result)
        if result is not None:
            if output_format == "json":
                if isinstance(result, UUID):
                    result = str(result)
                result = json.dumps(result, indent=4, sort_keys=True)
            print(result, flush=True)


def log_exception(args: argparse.Namespace, err: Exception) -> None:
    if args.verbose > 0:
        entry = traceback.format_exc()
        for x in entry.split("\n"):
            LOGGER.error("traceback: %s", x)
    LOGGER.error("command failed: %s", " ".join([str(x) for x in err.args]))


def execute_api(api: Any, api_types: List[Any], version: str) -> int:
    builder = Builder(api_types)
    builder.add_version(version)
    builder.parse_api(api)
    args = builder.parse_args()

    if args.verbose == 0:
        logging.basicConfig(level=logging.WARNING)
        api.logger.setLevel(logging.INFO)
    elif args.verbose == 1:
        logging.basicConfig(level=logging.WARNING)
        api.logger.setLevel(logging.INFO)
        logging.getLogger("nsv-backend").setLevel(logging.DEBUG)
    elif args.verbose == 2:
        logging.basicConfig(level=logging.INFO)
        api.logger.setLevel(logging.DEBUG)
        logging.getLogger("nsv-backend").setLevel(logging.DEBUG)
    elif args.verbose >= 3:
        logging.basicConfig(level=logging.DEBUG)
        api.logger.setLevel(logging.DEBUG)

    if not hasattr(args, "func"):
        LOGGER.error("no command specified")
        builder.print_nested_help(args)
        return 1

    if args.query:
        try:
            expression = jmespath.compile(args.query)
        except jmespath.exceptions.ParseError as err:
            LOGGER.error("unable to parse query: %s", err)
            return 1
    else:
        expression = None

    call_setup(api, args)

    try:
        result = call_func(args.func, args)
    except Exception as err:
        log_exception(args, err)
        return 1

    output(result, args.format, expression)

    return 0
