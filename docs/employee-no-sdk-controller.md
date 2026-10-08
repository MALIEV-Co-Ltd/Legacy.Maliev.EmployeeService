# Retained-descriptor no-SDK caller

`scripts/employee_no_sdk_controller.py` is a stdlib-only entrypoint compatible
with isolated Python (`-I`). It loads the exact reviewed custody source, checks
the independently supplied original policy hash and live hosted repository,
head, run and attempt, validates the actual boot/source and finite lease, then
calls the existing qualifier in-process with the caller's retained descriptor.
It creates no directories, authority, signing keys, SDK units or providers.

Inputs are `--policy`, `--policy-sha`, `--evidence` (an absolute label), and
`--evidence-fd` (an actual root caller's retained directory descriptor).
The original qualifier duplicates and validates the descriptor, checks fresh
physical memory >=4194304 KiB and the complete executable census, and preserves
its existing phase2..30 seconds, manager caps and120-second cleanup reserve.
No path-only call or automatic privilege escalation exists.

An independently trusted same-live-allocation Root handoff must supply the
original policy/pin and qualified root-owned descriptor before this entrypoint
may be invoked. A supplied hash proves byte identity; environment equality
proves only the checked process context. Neither proves independent approval.
Those external admission prerequisites remain absent. No workflow dispatch,
sudo command or trusted-root enrollment is implemented or performed here.

The Linux source workflow exercises135 controls: all117 previous controls and
18 caller controls, with zero skips and explicit descriptor/directory cleanup.
The new physical check delegates a real directory descriptor through both
callers while modeling the manager; it is not systemd/runtime qualification.
Negative controls cover allocation drift, expired policy, foreign source/boot,
SDK-policy substitution, missing/boolean FD, wrong platform/UID and pin drift.

Frozen baseline12/candidate15/full371 acceptance remains unrun. The existing
SDK/provider authority and independent-watchdog connection remain closed.
Ordinary application361 and these source controls cannot close original source
obligations or establish backend/provider parity.
