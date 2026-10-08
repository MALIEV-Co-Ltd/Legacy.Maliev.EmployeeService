# Employee Linux custody source

This source introduces a concrete Linux backend and an executable disposable
qualification route. It has not run on Linux and is not a backend qualification
receipt, Root enrollment, SDK grant, or provider qualification.

`backend_class(sealed_adapter_bytes)` loads the unchanged SDK adapter SHA
`8618a2ca9bd01f02e108303b78cfafd9a8b68d6a987026381e24bb7ea5c77d69` from
already verified bytes. The returned class exposes `sdk_adapter`; callers must
use its `SDKOwner` so the adapter's backend stage installation sees the same
Python class generation. The methods retain the original backend interface.

SDK entrypoints additionally require an original private detached-authority
envelope. `TRUSTED_ROOT_PUBLIC_KEY_SHA256` remains None and refuses before any
SDK/watchdog allocation. The verifier checks the unchanged original crypto
source, signed exact SDK policy/stage grant/backend qualification and a distinct
signed custody attestation binding this helper source and the reviewed non-native
hash inventory. No key is generated or enrolled. The envelope is read privately;
resource ledgers retain only its bound hashes. Direct watchdog candidate/provider
phases remain refused until the actual baseline-review and transport bridges are
independently qualified. This is not automatic grant-schema conversion.

The backend implements typed busctl properties, fresh two-pass complete `/proc`
birth/executable/inode/SHA census, physical `MemFree` and exact cgroup subtree
readback. A separately reviewed immutable non-native executable hash allowlist
is required. Unknown, deleted, unreadable or changing processes fail admission;
no process-name exclusion certifies completeness. Executable hashing has an
aggregate 512 MiB bound. The original SDK unit plan retains phase <=600 seconds,
allocation <=3600 seconds, physical 4 GiB admission floor, 3 GiB SDK memory,
swap zero, CPU 25%, 128 tasks and 120-second cleanup reserve.

Before SDK dispatch, `register_intent` fsyncs a private original intent and starts
a separate bounded systemd custody service. Its Python watcher owns the actual
SDK start, retains controller and SDK pidfds, observes invocation/cgroup/kernel
generation and writes an independent start witness. Controller loss, phase
expiry and explicit release trigger exact generation cleanup. The service uses
256 MiB, zero swap, CPU 25%, 16 tasks and RuntimeMax=phase+155 seconds. SDK cleanup
precedes watcher release. Manager helpers reuse the sealed finite helper source
with actual process handles, pidfds and all-member cleanup. Aggregate helper
wall time is 90 seconds, with 30 reserved for settlement; no broad foreign kill
or global provider cleanup exists.

The actual Linux no-SDK route is:

```text
sudo /usr/bin/python3 -I scripts/employee_linux_custody.py --mode qualify-no-sdk --adapter /private/sealed/employee_linux_sdk_owner.py --evidence /private/owned/evidence --policy /private/original-qualification.json --policy-sha EXACT_REVIEWED_RAW_SHA
```

The evidence directory must already be absolute/private. Policy is closed:
schemaVersion=1, purpose=`employee-disposable-no-sdk`, exact sourceHead, runId,
attempt, bootId, sourceSha256, startsUtc, expiresUtc, phaseSeconds (2..30) and
approvedNonNativeExecutableSha256. Its raw SHA must be independently reviewed
and supplied by the protected qualification consumer; matching a caller-chosen
hash alone is not Root authentication. Policy cannot authorize SDK/provider
commands. The route runs only a fixed Python sleep payload in a real capped
service, observes typed properties/cgroup/PIDfd, verifies actual finite expiry,
then settles the same manager generation and writes receipts. No user argv,
shell, .NET, Docker, database or network listener is exposed.

The disposable route qualifies only observed containment/typed readback/expiry.
It does not automatically certify controller-loss, SDK fast exit, uncertain
manager ACK recovery, complete hosted executable-inventory review, provider
transport, or cryptographic Root authority. If start acknowledgement is lost
before a positive invocation generation is retained, the independent custodian
writes `custody-uncertain.json`; it does not adopt an inventory/name match or
invent a successful start. Manager RuntimeMax still bounds the requested SDK
unit, but immediate exact settlement in that ambiguity requires a separately
qualified retained manager event/job witness. This remains an admission blocker.

No local Linux qualification was executed. The new pure controls exercise typed
installed scalar/message layouts, generation refusal, original finite input,
hash inventory classification, unstable census, memory semantics, argv/caps,
exclusive receipts and unsupported platform refusal. Source mocks never set
runtimeQualified=true. The sealed SDK adapter, source capsules, application
fixtures and original business tests are unchanged. The existing bounded
read-only workflow is narrowly extended to check this source; it still runs
only the read-only manager probe after portable controls.

The independent watcher persists SDK cleanup in its separate immutable custody-sdk-cleanup.json receipt without waiting for its own terminal receipt. The controller retains the separate watcher-terminal handshake. Disposable qualification retains the observed invocation, PID and cgroup generation before containment assertions, so a containment refusal still performs exact-generation settlement. These are pure regression controls; the Linux paths remain unrun.

Controller and watcher helper receipts use distinct immutable actor namespaces. Their budgets are fixed at 45 seconds each, with 15 seconds each reserved for cleanup; the combined bound remains 90 seconds with a 30-second cleanup reserve.
