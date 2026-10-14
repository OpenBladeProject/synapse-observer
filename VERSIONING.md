# Versioning

`Directory.Build.props` is the version source of truth. The initial prototype is
0.1.0. Use Semantic Versioning: minor for a new contributor-facing capability,
patch for a compatible fix, and no bump for documentation or tests alone.

Every pull request records its version impact, exact version change, and reason.
The first implementation introduces the initial version; subsequent changes
calculate their bump from current main. Evidence schema versions and native
observer protocol versions are independent contracts. An incompatible event
layout must advance the native protocol and update producer/consumer together.

Keep user-visible changes under Unreleased in CHANGELOG.md and link the
implementing pull request before marking it ready. Release source and packaged
build-manifest revisions must agree. Never label an unsigned local artifact as a
signed or validated public release.
