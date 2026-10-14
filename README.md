# Synapse Observer

A standalone Windows evidence collector for adding Razer Blade support to
[OpenBlade](https://github.com/OpenBladeProject/openblade-core).

The intended workflow is: open the app, collect device information, follow a
few short recording steps in Synapse, and share one evidence ZIP.

The collector observes vendor activity. It does not control the laptop or
replay captured commands. Development and live-observation limits are recorded
in [AGENTS.md](AGENTS.md).

The first implementation is a prototype. Public distribution requires tested
Synapse coverage, reviewed privacy filtering, and release signing.
