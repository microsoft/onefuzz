# Coverage Filtering

Coverage recording for _uninstrumented_ targets can be configured by a
user-provided _filter list_. By default, and if no list is provided, block
coverage is recorded for all runtime-observed modules with identifiable
function symbols.

## Filter list format

The filter list is a JSON file that satisfies the following schema:

```
[
  <module-include | module-exclude>*
]
```

The filter list an array of objects that describe per-module rules.

A `<module-include>` looks like:

```
{
  "module": <regex>,
  "include": <Boolean | Array<regex>>
}
```

And a `<module-exclude>` looks like:

```
{
  "module": <regex>,
  "exclude": <Boolean | Array<regex>>
}
```

Note that the `module-` rules are polymorphic.

## Filter rule application

Filter rules are applied when the OneFuzz coverage recording code observes a
target load a new module. We must decide whether or not to record any coverage
for the module at all, and then for each function symbol that belongs to it.
Per-function filtering is currently only implemented on Linux.

By default, all modules and their symbols are _included_ in coverage. When
module filter rules are present, the _first applicable rule_ is used to filter a
module. A module rule is _applicable_ if the absolute filesystem _path_ of a
loaded module matches the `"module"` regex for a rule. If there is a match, then
the rule is applied. No additional rules are consulted for a module. This means
that at most _one_ explicit module rule applies.

## Rule semantics

The actual module filtering rules are defined by providing exactly one
`"include"` or `"exclude"` property. The value of each property can in turn be a
boolean or an array of strings that encode a regular expression. Let's elaborate
on this.

## Boolean rules

At a high level, you may want to exclude an entire module (and all its symbols)
from coverage recording.

This can be expressed in two equivalent ways:

```json
{
    "module": "libpthread.so",
    "exclude": true
}
```
Or:
```json
{
    "module": "libpthread.so",
    "include": false
}
```

With respect to symbol coverage recording, the above are equivalent to the following
regex-based rules:

```json
{
    "module": "libpthread.so",
    "exclude": [".*"]
}
```
Or:
```json
{
    "module": "libpthread.so",
    "include": []
}
```

We note this only for completeness. For these extreme cases, use the earlier boolean forms.
They are more direct and explicit, and help OneFuzz record coverage more efficiently.

Note that the `"module"` property is also regex-valued.
Since OneFuzz tracks all available modules and symbols by default,
the (implicit) default rule could be written as:

```json
{
    "module": ".*",
    "include": true
}
```

It is never necessary to write a rule equivalent to this.
However, this observation hints at how we can invert the default behavior!
If you want to exclude all modules by default, then we can add this rule to
the _end_ of our filter list:

```json
{
    "module": ".*",
    "exclude": true
}
```

As long as this is the last rule in our list, then any module not
filtered by some earlier rule will match this `"module"` regex, and thus be excluded.
