# Employee original-fixture compatibility source

Production Root/backend trust and scoped SDK binding remain absent. This slice preserves original C# tests, PostgreSQL-only production code, frozen capsules and immutable registry-v4/SDK owner. No SDK, Docker, Unix socket, provider or hosted qualification was run. Original owner 12/15/371 behavioral checks remain unrun.

## Primary wire evidence

Testcontainers4.10.0 resolves to commit d66a9f12a63d1082580270a310a77463ce01272c. Its project/NuGet nuspec pins Docker.DotNet.Enhanced3.131.1, whose nuspec resolves to testcontainers/Docker.DotNet commit5b41b336d0153d428805ecdea41c2c63afe5f2e7. Exact bounded primary reads/hashes are retained off-repository in outputs/employee-fixture-compatibility-source-review.

[Immutable endpoint configuration](https://github.com/testcontainers/testcontainers-dotnet/blob/d66a9f12a63d1082580270a310a77463ce01272c/src/Testcontainers/Configurations/AuthConfigs/DockerEndpointAuthenticationConfiguration.cs) proves default API1.44, x-tc-sid and tc-dotnet/<assembly-version>. [Immutable Docker client](https://github.com/testcontainers/Docker.DotNet/blob/5b41b336d0153d428805ecdea41c2c63afe5f2e7/src/Docker.DotNet/DockerClient.cs) derives Unix HTTP host from socket filename; ManagedHandler appends :80. This relay's host is docker.sock:80. It emits HTTP1.1, Docker.DotNet User-Agent, JSON null omission/nonnullable false booleans, application/json; charset=utf-8, exec TTY/ConsoleSize, hijack Connection upgrade/Upgrade tcp and query booleans0/1. Portable synthetic regressions cover these forms.

DefaultLabels includes static language/version plus both org.testcontainers.session-id and resource-reaper-session. Ryuk0.14.0 WithCleanUp(false) sets its reaper label to zero GUID while preserving static session-id. PostgreSQL retains original -c fsync=off / full_page_writes=off / synchronous_commit=off flags and literal Env JSON value bytes. Missing optional Redis Cmd/Env/Entrypoint stay omitted. Actual assembly-derived version label remains an observed policy value.

## Connected concrete source

employee_fixture_registry_compatibility.transform binds exact original/effective SHA, immutable image ID, full inspect Config/HostConfig projection hashes, daemon ID, SDK generation, dynamic name/session/expiry and explicit cap/custody label changes. SignedPlan authenticates domain-separated detached Ed25519 with independently enrolled Root pin (currently None) and rechecks original signature/bytes before create. No spontaneous raw-SHA enrollment/default expansion occurs. Ryuk privilege reduction/private socket mount needs explicit reviewed transformation; broad daemon mount never passes unchanged.

Journal fsyncs exclusive nonsensitive intent before forward; no raw body/Env/credentials are persisted, replay/adoption refuse. ActiveBudget enforces combined active memory/CPU/count and keeps ambiguous allocations charged. Frozen cap envelope is memory<=1GiB each, equal MemorySwap, NanoCpus<=1e9, PidsLimit<=256, no privilege, CapDrop ALL/no-new-privileges, isolated modes/loopback ports. Specific limits come from reviewed plan; no assumed512/256MiB defaults. Every live phase requires fresh physical memory>=4194304KiB. Budget releases only after exact healthy owned absence.

UnixForwarder.request contains actual AF_UNIX connect/send/read source with socket device/inode/UID/GID/mode before/after, literal SO_PEERCRED and PID birth/boot/unified-cgroup readback plus pidfd liveness. Absolute deadlines and250ms receive polling check inherited controller pipe. Headers<=8192/request<=65536/aggregate stream<=1MiB. Chunked and Docker multiplex outputs are bounded and preserved. Anonymous public-registry AuthConfig can pass byte-for-byte; credentials refuse. Production constructor refuses before any socket.

OriginalFixtureBackend.dispatch connects durable custody to create/start/stop/inspect/wait/exec-create/exec-start/exec-inspect/log/delete/image-inspect/pull/list/container-list/events/attach. Container operations require retained full ID/raw lexical Created/image/name/static+session+custody labels, full signed Config/HostConfig readback hashes, no persistent/foreign mounts and tracked exec IDs. Ambiguous mutations quarantine without retry/adoption. Original foreground disposal binds fresh live SDK custody; Ryuk/controller-loss cleanup additionally requires actual SDK-retired/client-empty. Successful retirement requires actual delete ACK, full-ID404, healthy exact daemon ID and full unique exact-ID inventory absence;404 alone refuses.

PrivateRelay.run creates nonce-owned0700 directory/0600 socket, derives only DOCKER_HOST from its path and finally closes pidfds/connections/listener. Cleanup checks exact retained identities before unlink/rmdir and preserves replacements. SDK child testhost peers need same boot/UID/GID/exact cgroup and bounded ancestry to retained live SDK root. Exact Ryuk peer generations are separate. Inherited owner pipe and lease/deadlines stop dispatch after controller death. It currently serializes connections/backlog1; concurrent/long-lived Ryuk stream semantics remain unqualified.

RyukFilters.register durably records the exact original reaper-session filter before ACK. Cleanup maps only matching retained generations, excludes Ryuk itself and calls retired-owned deletion. Python protocol controls do not replace/disable the actual Ryuk TCP channel or prove Go Docker wire compatibility.

## Executable frozen SDK successor

sdk_successor_source(exact_old_sdk_bytes) produces a complete executable postimage with four exact single-occurrence edits. Preimage SHA8618a2ca9bd01f02e108303b78cfafd9a8b68d6a987026381e24bb7ea5c77d69 is mandatory; original stays untouched. Edits are planned scoped Environment, actual manager Environment equality to expected properties, fixtureEnvironmentPolicySha256 backend receipt binding, and exact authenticated transport guard replacing blanket provider block. Fixed argv/pins/3GiB SDK/CPU25%/custody/leases/cleanup stay unchanged. Emitted _SCOPED_RELAY_BINDING=None and original Root trust remain absent.

ScopedSdkBinding authenticates environment attestation in a separate Ed25519 domain; binds stage without grant/backend circular hashes, successor/compatibility code SHA, exact private socket/time and original Root grant/backend receipt hashes. It emits only DOCKER_HOST=unix://<owned>/docker.sock. No API override, broad endpoint, socket override or Ryuk disable. A synthetic binding cannot grant qualification. Complete postimage/unified diff are retained off-repository for review, not applied to immutable adapter.

prepare_consumption(stage_bytes, old_sdk_bytes, original_request_bytes, plan_bytes, relay_path, utc_now) actually validates the sealed old unit_plan and preserves frozen original focused/full argv. It connects transformation/registry/forwarder/dispatcher/relay/retirement APIs, reports old Environment=[]/provider block and successor/environment requirements.

```text
python3 -B scripts/employee_fixture_compatibility.py --mode source-check
python3 -B scripts/employee_fixture_compatibility.py --mode prepare-consumption --stage STAGE.json --sdk-source scripts/employee_linux_sdk_owner.py --request ORIGINAL.json --plan REVIEWED_PLAN.json --relay-path /PRIVATE/employee-fixture-NONCE/docker.sock --evidence /OWNED/source-connection.json
```

Preparation reads bounded exact inputs and writes exclusive false-authority receipt. --mode qualification refuses before reading paths or creating resources. Parent-owned protected workflow consumption may exercise this source route; no workflow edit/dispatch was made by this slice.

## Validation and unrun boundaries

AST compile and92 portable unittests passed under retained Windows process/private256MiB CPU25% job,25s timeout and128KiB output cap. The review successor covers aggregate finite retirement, continuing remaining exact-owned cleanup after a failure, immediate pre-forward lease checks, completed deletion existence reads with healthy full inventory, and SDK binding initialization before CLI entry. Fake sockets/observations/synthetic fixtures and pure sealed SDK unit_plan/postimage controls are not actual native receipts. No applicable C# build was run: no C# edits, native SDK explicitly unauthorized. No repository commit/push applies here.

Runtime requires independently enrolled Root, original signed transformation/environment policies and actual manager/daemon/socket/SDK/testhost/Ryuk qualification. Immutable image IDs/expanded default projections and actual original readiness-command pins need a qualified producer. Ryuk's Go API/header/filter forms, concurrency/live disconnect, same-UID pathname lstat/unlink races, owner pipe FD replacement/hard helper loss, AutoRemove races, manager/cgroup inode ancestry attestation and interrupted mutation recovery remain explicit blockers. Original .NET client wire source is now sealed; actual native wire and full original suite are unrun. This packet does not claim full live Testcontainers compatibility. Setup/compiler/provider/transport/timeouts are never behavioral RED. Original business IDs/Auth/acceptance evidence remains intact.
