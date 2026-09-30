# Employee issue24: nullable self-profile phone

## Owned scope and exact baseline

Worktree: `B:/maliev-legacy/.worktrees/employee-self-profile-null-phone-20261001`.
Branch: `codex/employee-self-profile-null-phone-20261001`.
Employee main/base: `7b15f78958626b4d929082f250ee5257d2dc7240`.
Only this plan and new `Legacy.Maliev.EmployeeService.Tests/EmployeeSelfProfileNullablePhoneTests.cs`
are authorized initially. Existing tests, runtime, Auth, schema, configuration,
consumer browser, CI and all other worktrees remain untouched. No commits,
pushes, notifications, deployment or persistent/source data operations.

Private clones match `.github/workflows/_build-and-test.yml` exactly:

- Defaults: `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`.
- Contracts: `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`.
- Both are clean detached clones under this worktree's ignored `.dependencies`.

An initial owned-only build used newer Defaults515c589/Contracts47f8f94. Its
0W0E/16-pass result is retained but explicitly excluded from acceptance. Root
corrected the pins before new tests; fresh exact-pin build/focus follows below.
No sibling build outputs or source files were touched or removed.

## Source and consumer traceability

Committed source mirror:
`B:/maliev-legacy/.artifacts/source-commit-mirror-20260930.git`.
Checkpoint: `bed10c7d15e0698e0b75f1329d0f312937f5d77f`.
Original optional phone fields originate in
`5fac706a7983a6d359b39acbd670e6800afe020e`: Employee.PhoneNumber and the
Intranet employee View field are not required; the source employee update
assigns the submitted nullable phone directly. Source identity mutations in
that page are deliberately not copied into EmployeeService.

Legacy narrow self-profile route and the offending validation expression were
introduced in `0e2e8d6c35cee95857a8b6f324623c4bf4530fdc`. Current producer DTO
`UpdateEmployeeSelfProfileRequest` declares optional `string? PhoneNumber`;
repository persists `request.PhoneNumber?.Trim()` and clears the value naturally.
The controller predicate `request.PhoneNumber?.Length <= 256` is false for null.

Intranet consumer pinned at `2589c562815bbdea394a72e416325be220031b4e`:
`EmployeeProfile.razor` normalizes blank/whitespace phone to null, then posts its
four narrow fields through `PUT /bff/profile`. The BFF derives the selected
employee ID from its authenticated session and forwards a server service token
to `PUT /employees/{employeeId}/profile`. Its optional DTO agrees with the
producer's DTO. A null-phone save currently reaches producer validation and400.

Historical source failure-tracing commit
`f0640fe0719b2eb6becda378bff08153d955be07` is boundary context, not an additional
runtime change in this slice. Workflows checkpoint
`77e7be453469d269890f9524fd7ca14a6d2b6e24` remains157 resolved/941 unresolved;
this bounded bug fix cannot resolve whole initial/source-tracing commits.

Issue24 is the producer defect. Intranet#164 is the QA umbrella;#158 remains
future HR/admin maintenance, audit, concurrency and employment/offboarding work.

## Tests and authority boundaries

New tests use actual EmployeesController, application service, repository,
production MVC/JWT/permission middleware and PostgreSQL18. A uniquely named
Testcontainers PostgreSQL instance is created once by the class fixture,
migrated only inside that disposable instance and disposed afterward. Each test
seeds its own rows; no existing database or source SQL Server is contacted.

The HTTP factory stays in Production, uses ephemeral RSA keys and realRS256
validation with exact issuer/audience/permissions, and never replaces auth with
a fake handler or Testing's signature bypass. Keys/tokens are not persisted or
printed. Cache is explicitly disabled for Redis inside the test factory, using
the production-supported memory cache provider and real DistributedEmployeeCache;
cache-read/invalidation logic is not stubbed. There is no remote IAM registration.

24 cases:

- 3 actual-controller cases: null, empty and nonempty phone; persisted fields.
- 2 HTTP clearing cases: explicit null and omitted PhoneNumber.
- 1 HTTP absent-phone/name-change case.
- 1 real warm-cache/clear/readback case (null property omitted in PascalCase GET).
- 2 HTTP phone boundaries: length0/256.
- 5 invalid whitespace/oversized name or length257 phone controls.
- 4 expanded owner/admin JSON rejection controls, with both employees unchanged.
- 4 anonymous, missing permission, expired JWT and wrong signature controls.
- 1 selected-employee/nonselected-row preservation control.
- 1 valid missing-employee404 control.

Email, role, address ID, creation timestamp and other employee rows remain
unchanged. The route trusts the authorized BFF's selected employee ID; it does
not implement employee-sub equality. Expired JWT tests characterize stale
authority; warmed-cache tests characterize stale reads. No expected-version or
stale-edit detection is invented. Concurrent stale profile edits remain#158.

## Observed RED and commands

All commands set `DOTNET_PROCESSOR_COUNT=1`, `GITHUB_ACTIONS=false`,
`UseLocalMalievDependencies=true` and absolute
`MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-self-profile-null-phone-20261001/.dependencies`.

Build: `dotnet build Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release`
plus explicit `-p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=<owned absolute .dependencies>`.
Every displayed Release DLL path is within this owned worktree.

Fresh exact-pin baseline build:0warnings/0errors,2.92s. Existing focus filter:
`FullyQualifiedName~EmployeeControllerContractTests|FullyQualifiedName~EmployeeSelfProfilePostgresTests|FullyQualifiedName~EmployeeJwtExternalizationTests`.
Result16/16passed,0failed/0skipped,9s.
Artifact: `TestResults/nullable-phone-exact-pin-baseline/baseline-focus.trx`.

New-test Release build:0warnings/0errors,2.03s. RED command:
`dotnet test Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release --no-build --no-restore`
plus the same private properties and
`--filter FullyQualifiedName~EmployeeSelfProfileNullablePhoneTests`.
Result5failed/19passed/0skipped,24total. All five failures are exactly expected
204/NoContent versus actual400/BadRequest, not infrastructure/setup errors.
Artifact: `TestResults/nullable-phone-red/nullable-phone-red.trx`.

After whitespace-only formatting in the new test file, repeated private Release
build remained0warnings/0errors,2.01s. Final repeated RED remained5failed/19passed,
0skipped,24total, with identical five assertion failures.
Artifact: `TestResults/nullable-phone-red-final/nullable-phone-red-final.trx`.
Scoped whitespace verification, `git diff --check` and redacted Gitleaks passed
(21863 bytes, no leaks before this evidence-only paragraph). Docker readback
showed no remaining `employee-null-phone-*` container after test disposal.

## Explicit implementation gate and remaining validation

At the RED handoff, no runtime fix had been made. Root reviewed the complete
tests, plan and exact-pin RED, then approved only replacing the final controller
predicate with `(request.PhoneNumber is null || request.PhoneNumber.Length <= 256)`.
Keep every other name validation, route, permission, DTO, persistence/cache and
serialization rule unchanged. Do not coerce null to an empty string in consumer
or producer and do not broaden accepted administrative fields.

After authorization: single-expression repair; fresh privateRelease0W0E;
focused new+existing tests; complete Employee suite; scoped formatting,
vulnerability audit and redacted Gitleaks; root independent diff/build/test
acceptance. Intranet real producer/BFF/browser integration is a separate later
gate and has not been executed here. No full Aspire/container/deployment or
production acceptance is claimed.

## Final producer candidate evidence

The sole runtime change is that approved self-profile predicate. No other
controller validation, authority, DTO, serializer, cache, persistence, schema,
configuration or existing test changes were made.

Fresh exact-private-pin Release build:0warnings/0errors,3.05s. Combined new and
existing controller/JWT/PostgreSQL focused tests:40/40passed,0failed/0skipped,15s.
Complete EmployeeService suite:75/75passed,0failed/0skipped,12s. The full suite is
51existing plus24new cases; it is not a partial filtered run.

- `TestResults/nullable-phone-green-focus/nullable-phone-green-focus.trx`.
- `TestResults/nullable-phone-green-full/nullable-phone-green-full.trx`.

The five previously observed RED assertions now pass, including null/omission
persisted clearing and fresh readback after cache warming. Signed JWT negative
controls and name/length/expanded-payload controls remain passing. The tests use
the actual production HTTP service boundary and disposable PostgreSQL, not the
Intranet BFF/browser boundary. That consumer chain remains a later explicit gate.

Root owns independent acceptance/integration. No commits/push/GitHub mutations,
deployed writes or production/full-Aspire claims were made.

Final whole-solution `dotnet format Legacy.Maliev.EmployeeService.slnx
--verify-no-changes --no-restore` passed. Transitive vulnerability audit via
`dotnet list Legacy.Maliev.EmployeeService.slnx package --vulnerable
--include-transitive --no-restore` found no vulnerable packages across all five
projects. Final scoped diff/new-file Gitleaks and whitespace checks are performed
on this final document and candidate before releasing ownership.

Root independent acceptance: exact-private Release solution build passed with
zero warnings/errors; combined focused tests40/40 and complete suite75/75 passed
with zero skips. Root TRX files are ignored under `TestResults/root-acceptance`.
The first root format invocation incorrectly supplied an unsupported `-p` CLI
argument; it performed no formatting. The corrected invocation uses environment
MSBuild properties and the normal `--verify-no-changes --no-restore` options.
Consumer browser/Aspire acceptance is not inferred from these producer tests.
