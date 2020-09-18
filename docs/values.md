# Values for OneFuzz moving forwards

## Project Level Values

These are user-focused values for OneFuzz moving forwards, ordered in priority.
It is better to sacrifice something later to achieve a higher priority value.

1. Debuggability. Enable the user to inspect, understand, and address their
   entire fuzzing workflow.
1. Composability. Enable the The ability to create a workflow combining multiple
   parts into a more complicated part.
1. Extensibility. Enable the user to extend the fuzzing infrastructure to meet
   their needs without requiring our assistance.
1. Fuzzing Engine Performance. Enable the fastest bug finding capabilities to be
   deployed.
1. Security. User's software, data, and results should be protected from
   adversaries.
1. Approachability. Users should be able to onboard new software to be fuzzed
   into their CI/CD pipeline easily.

## Project Level Non-Values

All things being equal, these values, while nice to have, are of significantly
less importance than those previously discussed.

1. High-Availability. While an important component for the SDL of any project,
   fuzzing is not a business critical task.
1. Thoroughness. Every use case does not need to be covered from the onset of
   OneFuzz.

## Implementation Ideals

1. Rely directly on Azure services and infrastructure as much as possible.
1. Reduce our software install burden on fuzzing nodes
1. Support large number of OS distributions & versions