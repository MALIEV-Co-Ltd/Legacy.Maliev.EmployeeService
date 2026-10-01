# Employee directory source parity — issue 26

## Independent root acceptance

Root inspected the full new fixture/tests, original source controller, current
consumer query mapping and sole repository-method diff. A solution build first
passed but selected Debug outputs for omitted private dependency projects;
acceptance was repeated through the Release test-project graph so both pinned
dependencies and all owned projects used Release outputs. Final build had zero
warnings/errors, focused 26 and unfiltered full 101 tests passed with zero skips.
Artifacts: `TestResults/root-directory-exact-release-focus` and
`TestResults/root-directory-exact-release-full`; raw Cobertura again reports
API 13.89%, Application 48.38%, Data 84.27%, Domain 63.88%. This does not meet or
waive broad coverage acceptance, now tracked separately in issue #27.
Whole-solution formatting, five vulnerability audits, changed-file safety and
redacted test/document secret scans passed. Existing tests/config/schema remain
unchanged. No persistent data, external notification or application deployment
was performed. Protected PR and exact-main CI are still required before closure.

## Ownership and gate

Bounded runtime candidate, frozen for independent root acceptance. Base `48f00de25a31a033267b5a9e34146bfd5c4deb2a`, branch `codex/employee-directory-parity-20261001`. Initially TEST/DESIGN ONLY; root approved the sole `EmployeeRepository.GetEmployeesAsync` repair after reading the genuine RED and source mapping. Only that method, two new tests/fixture files and this document changed. No existing tests, configuration, schema, contracts, other worktrees, GitHub, commits or deployments changed. Disposable PostgreSQL 18 and Redis 7.4 are isolated and destroyed by fixture teardown. This is not production-derived Aspire acceptance or whole-owner completion. Issue 25 nullable-phone behavior remains untouched; Auth identity authority is excluded.

Private absolute dependency root: `B:/maliev-legacy/.worktrees/employee-directory-parity-20261001/TestResults/.private`. Detached clean Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`, matching Employee CI. All dependency outputs are Release inside that root.

## Committed-source mapping and consumer

The immutable source mirror checkpoint is `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Read-only committed `Maliev.EmployeeService.Api/Controllers/EmployeesController.cs` at both `5fac706a7983a6d359b39acbd670e6800afe020e` and `72eb9f1949176392141951d35e6e06f7c30af4c2` implements `GET /Employees`:

| Boundary | Source contract | Current disposition |
| --- | --- | --- |
| Numeric search | `int.TryParse` ternary selects ID equality **instead of** text fields | Genuine RED: current OR also admits name/phone/email matches |
| Non-numeric search | Lowercase literal `Contains` across phone/full/first/last/email | Genuine RED for `%` and `_`; current ILike interpolates wildcard pattern |
| Empty selected page | Missing result **or Items.Count == 0** returns 404 | Genuine RED: total > 0 returns 200 empty Items |
| Sort | ID ascending default, ID descending, email ascending/descending | All four passing controls |
| Projection | HomeAddress included, relationship cycles cleared | PascalCase/nested country/address/null omission passing control |
| Paging | Historical unbounded default | Deliberately retain current bounded default 50, max 250 and lower clamp 1; passing controls |

Credential validation source `f32adc66af8d7ba66fc7b803a7f5291349c11932` is Auth-owned and excluded. No identity/password projection was added.

Current target flow: `Api/Controllers/EmployeesController.cs` list action → `Application/EmployeeApplicationService.cs` clamp/default → `Data/EmployeeRepository.cs` `GetEmployeesAsync` count/project/skip/take. Exact permission is `legacy-employee.employees.list`; routes/query/paging DTO unchanged.

Read-only current Intranet `Legacy.Maliev.Intranet/Employees/LegacyEmployeeClient.cs` `GetEmployeesAsync` sends `/employees?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}`, explicit bearer, forwards cancellation and maps 404 to null. Literal expectations are encoded through the same URI escaping boundary; no consumer change is proposed. This is API bearer auth, not a new cookie/browser/CSRF path.

## Actual evidence and assertion reach

The fixture uses actual Production Program registration, real fixture-specific RS256 issuer/audience/signatures, normal permission middleware and real PostgreSQL repositories. Redis is enabled and connected to actual Redis 7.4. No authentication scheme, permission handler or IAM authority is replaced. Interceptor only coordinates caller cancellation at the actual Employee query; it neither returns rows nor changes authority.

- Unchanged baseline: Release zero warnings/errors; 75 passed, 0 failed/skipped.
- Initial new focus: 25 cases = 6 assertion failures, 19 passes, 0 test errors/skips.
- Final fresh Release: zero warnings/errors; final new focus 26 = 6 assertion failures, 20 passes, 0 errors/skips.
- Final unfiltered suite: 101 = 95 passes (all original 75 plus 20 controls), 6 same assertion failures, 0 errors/skips.
- Each query helper checks a fresh independent PostgreSQL snapshot **before** its behavior assertion. Thus all six RED cases prove unchanged persisted employee/address values before failing. No test merely fails at fixture startup/authentication.
- `7`: actual `[7,8,9,10]`, expected `[7]`; `007` remains a passing numeric control.
- Absent ID `7000`: actual 200, expected 404 despite phone/email 7000.
- `Sales%Lead` and `Code_1`: actual `[1,2]`, expected `[1]`.
- Literal `%` page: exact page IDs pass, then TotalPages is 3 instead of 2; current wildcard matches the ordinary third row.
- Selected empty page: actual 200, expected 404.
- Backslash and combined `%_\\` controls already pass; do not claim these are separately broken.
- Passing controls: English case/trim, Thai substring, every source sort flag, current default/caps, PascalCase and nested HomeAddress country 764, null omissions, absent text 404, anonymous/wrong RSA/expired 401 and absent permission 403 with no PII/database effects.
- Cancellation test waits for real query entry, aborts HTTP, asserts `OperationCanceledException`, observes propagated server cancellation, fresh unchanged PostgreSQL and a subsequent successful real request. Its 10-second waits are safety bounds, not an expiry/sleep gate.

Artifacts retained under `TestResults`:

- `directory-baseline/natth_MALIEV-31USFIV_2026-10-01_10_48_27_net10.0.trx`
- `directory-red-focus/natth_MALIEV-31USFIV_2026-10-01_10_51_52_net10.0.trx` (initial)
- `directory-red-full/natth_MALIEV-31USFIV_2026-10-01_10_54_16_net10.0.trx` (pre-snapshot-order strengthening)
- `directory-frozen-red-focus/natth_MALIEV-31USFIV_2026-10-01_10_55_53_net10.0.trx`
- `directory-frozen-red-full/natth_MALIEV-31USFIV_2026-10-01_10_56_06_net10.0.trx`
- `directory-final-red-build.binlog`
- `directory-frozen-red-full/ed93e96e-7c32-4cc0-a706-829d36e3f55b/coverage.cobertura.xml`

Unexcluded raw line coverage from that final RED suite: API 13.89%, Application 48.38%, Data 84.20%, Domain 63.88%; shared Defaults 18.94%, Contracts 0%. API/Application 80% gate is **not met or waived**; this bounded parity test lane does not claim broad coverage acceptance.

## Historical requested repair and approved implementation

Root explicitly approved this repair after frozen RED/static terminal. Implemented only in `Data/EmployeeRepository.cs` `GetEmployeesAsync`:

1. Numeric branch uses only exact employee ID equality, not numeric OR text.
2. Non-numeric branch uses literal substring matching (escape SQL pattern characters, including escape character, or verified translated literal case-insensitive Contains), retaining exactly existing searched fields, current trim/case behavior and PostgreSQL parameterization. Avoid new regex/fuzzy/identity search.
3. After selected projection, return null for no selected Items so existing controller produces 404, regardless of positive total.

Preserve all four sorts, bounded paging, projection, every mutation/cache path, routes/DTOs, permissions, normal RS256 and current self-profile validator. No schema/config/default changes, no Intranet edits. Acceptance after approved repair requires fresh Release0W0E, 26/26 focus, complete 101/101 suite plus original controls, unchanged unexcluded coverage reporting, whole format/audits/scans. No source-owner/Aspire closure inferred.

## Final GREEN candidate evidence

Fresh Release build `TestResults/directory-green-build.binlog`: zero warnings/errors. New focus 26/26 passed with zero skips; full 101/101 passed with zero skips. Explicit TRX test-name comparison confirms all original 75 baseline cases are present and passed (0 missing/not-passed). Runtime diff is confined to the list method: numeric conditional, explicit `ILIKE ... ESCAPE '\\'` parameters with backslash/percent/underscore escaped in that order, and selected empty-items null. All six original regression assertions are unchanged and now pass, including independent PostgreSQL snapshots.

- Focus: `TestResults/directory-green-focus/natth_MALIEV-31USFIV_2026-10-01_10_58_54_net10.0.trx`
- Full: `TestResults/directory-green-full/natth_MALIEV-31USFIV_2026-10-01_10_59_22_net10.0.trx`
- Coverage: `TestResults/directory-green-full/6aeb4d29-6ada-4a61-b98a-be5053a8e7c0/coverage.cobertura.xml`
- Unexcluded GREEN line rates: API 13.89%, Application 48.38%, Data 84.27%, Domain 63.88%, shared Defaults 18.94%, Contracts 0%. API/Application80 remains unmet, not waived. This is a meaningful scoped parity repair, not a whole-owner coverage gate.

GREEN commands use the same explicit private properties as RED, changing only `-bl:TestResults/directory-green-build.binlog` and results directories to `directory-green-focus` / `directory-green-full`; full still collects `XPlat Code Coverage`. Whole-format, all five vulnerability audits, Data/tests/docs secret scans, history scan and diff checks are repeated/verified before frozen handoff. Root independent verification remains required; no commit was created.

## Commands and static checks

All commands use this worktree and absolute private root:

```powershell
dotnet build Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-directory-parity-20261001/TestResults/.private -bl:TestResults/directory-final-red-build.binlog
dotnet test Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-directory-parity-20261001/TestResults/.private --filter FullyQualifiedName~EmployeeDirectoryParityHttpTests --results-directory TestResults/directory-frozen-red-focus --logger trx
dotnet test Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release --no-build -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-directory-parity-20261001/TestResults/.private --results-directory TestResults/directory-frozen-red-full --logger trx --collect:'XPlat Code Coverage'
$env:UseLocalMalievDependencies='true'
$env:MalievWorkspaceRoot='B:/maliev-legacy/.worktrees/employee-directory-parity-20261001/TestResults/.private'
dotnet format Legacy.Maliev.EmployeeService.slnx --no-restore --verify-no-changes
foreach ($name in @('Api','Application','Data','Domain','Tests')) { dotnet list "Legacy.Maliev.EmployeeService.$name/Legacy.Maliev.EmployeeService.$name.csproj" package --vulnerable --include-transitive }
gitleaks dir Legacy.Maliev.EmployeeService.Tests --redact=100 --no-banner --no-color
gitleaks git . --log-opts=-30 --redact=100 --no-banner --no-color
git diff --check
```

Whole format passed, all five package vulnerability audits returned no vulnerable packages, scoped tests scan and available 20-commit history scan returned zero leaks. Final documentation scan/readback and final diff/pin checks recorded at handoff. No live build/test handles remain after release.
