# Employee #27 route acceptance — tests/design gate, 2026-10-01

## Current status and ownership

Exclusive workspace `B:/maliev-legacy/.worktrees/employee-route-acceptance-20261001`, branch `codex/employee-route-acceptance-20261001`, base `15573fb1e10a515554dd2ed91b7fe12f5fc7051a`. After root observed four genuine REDs, the approved single application runtime method now reads detail from the authoritative repository without positive cache Get/Set. One explicitly reviewed old cache-policy test is adapted to distinguish stale cache from fresh repository result; all other old assertions remain unchanged. After a separate observed documentation RED, API-local HTTP JSON options are aligned with unchanged MVC PascalCase/null-omission options. Contracts, schema, permissions and shared Defaults remain untouched. No commit, push or external/provider writes. PostgreSQL 18 and Redis 8 containers contain disposable synthetic fixtures, not copied staff data.

Final verification: Release 0 warnings/0 errors, new focus 63/63 passed, full 164/164 passed, zero skips. Format verification, package vulnerability audit, redacted gitleaks and diff whitespace checks pass. Raw API coverage is 29.67% (100/337 lines), below the required 80%; Application is 100%, Data 95.79%, Domain 100%. The API denominator includes 237 generated XML-comment helper lines at zero hits; handwritten API is 100/100. No exclusions or threshold changes. Therefore quality #27 remains open and no whole source-owner/deployed-grant acceptance is claimed; bounded correctness/documentation slices await root independent review.

Historical TDD chronology: existing baseline 101/101; initial route controls 56/56; real Redis race 4/4 RED and combined 157 GREEN/4 RED of 161; approved database-authority repair then 60/60; actual documentation HTTP PascalCase mismatch 1 RED; approved API-local schema-option repair plus poison/read-denial/removal-denial and all ten DTO schemas then 63/63 and full 164/164 GREEN. Compiler-only xUnit2031 assertion overload correction was not product RED; no old expectation was weakened beyond the explicitly approved single authority-policy replacement.

## Action, permission and wire matrix

All routes use normal Production JWT RS256 authentication and actual permission middleware. Prefix permission names with `legacy-employee.`. Global means the current attribute has no resource template; scoped-resource testing is fixture-only enabled. Every DELETE requires an actual live IAM check and is critical. Positive IAM responses below are a controlled named primary transport through the actual `IamServiceClient`, not deployed IAM authorization proof.

| Method/path | Permission suffix | Resource | Wire/outcome |
| --- | --- | --- | --- |
| POST /employees | employees.create | global | Employee body; 201 GetEmployee Location |
| GET /employees/{employeeId} | employees.read | /employees/{employeeId} | PascalCase employee, HomeAddress only; 404 absent |
| GET /employees | employees.list | global | sort/search/index/size, bounded existing pagination |
| PUT /employees/{id} | employees.update | /employees/{id} | Administrative body; 204/404 |
| DELETE /employees/{id} | employees.delete | /employees/{id} | Live; 204/404 |
| PUT /employees/{employeeId}/profile | employees.self-update | /employees/{employeeId}/profile | Narrow first/last/phone only; 204/404 |
| POST /employees/addresses | addresses.create | global | Optional address fields; 201 GetAddress Location |
| GET /employees/addresses/{addressId} | addresses.read | /employees/addresses/{addressId} | PascalCase address; 404 absent |
| GET /employees/addresses | addresses.list | global | Array; empty source collection 404 |
| PUT /employees/addresses/{addressId} | addresses.update | /employees/addresses/{addressId} | Address body; 204/404 |
| DELETE /employees/addresses/{addressId} | addresses.delete | /employees/addresses/{addressId} | Live; 204/404 |
| POST /employees/roles | roles.create | global | Optional name/description; 201 GetRole Location |
| GET /employees/roles/{roleId} | roles.read | global | PascalCase role; 404 absent |
| GET /employees/roles | roles.read | global | Array; empty source collection 404 |
| PUT /employees/roles/{roleId} | roles.update | /employees/roles/{roleId} | Role body; 204/404 |
| DELETE /employees/roles/{roleId} | roles.delete | /employees/roles/{roleId} | Live; 204/404 |
| POST /employees/{employeeId}/signatures | signatures.write | /employees/{employeeId} | Query bucket/objectName; 201 GetEmployeeSignatureImageFile Location |
| GET /employees/signatures/{employeeId} | signatures.read | /employees/{employeeId}/signature | Signature metadata only; 404 absent |
| PUT /employees/signatures/{employeeId} | signatures.write | /employees/{employeeId}/signature | Bucket/ObjectName/optional EmployeeId body; 204/404 |
| DELETE /employees/signatures/{employeeId} | signatures.delete | /employees/{employeeId}/signature | Live; metadata-only 204/404 |

Source has 19 nonidentity business actions; the restricted self-profile route is a separately accepted current architecture addition. Identity CRUD and credential validation belong to Auth, not this bundle. No signature-list or other invented DELETE route. Preserve PascalCase, omitted nulls, exact named Locations, CreatedDate/ModifiedDate semantics, optional address/role fields, source signature reassignment and current EmployeeId-based signature deletion correction. Source signature DELETE used primary-key FindAsync(employeeId); do not reintroduce that defect. Existing bounded pagination, computed FullName, UTC wall-clock timestamps and granular permissions are accepted architecture, not source regressions.

Current consumers inspected read-only: Intranet `Employees/LegacyEmployeeClient.cs` lines 13/24/34/45 uses list/detail/create/delete; BFF `Employees/EmployeesProxy.cs` lines 13/23/38 uses detail/narrow profile/list. The BFF owns self-profile selection from the signed session identifier; API tests do not claim browser/session ownership proof. Legacy Web has no employee-route references in its current source search. Signature objects remain metadata: no GCS calls or storage proof.

## Individual source cohort and exclusions

Read-only bare mirror checkpoint `bed10c7d15e0698e0b75f1329d0f312937f5d77f`. Source producers are `Maliev.EmployeeService.Api/Controllers/{Employees,Addresses,Roles,Signatures}Controller.cs` plus source Startup serialization and EmployeeContext mappings. Ledger has 24 owner entries, not a blanket completion checklist:

| Full SHA | This bundle treatment |
| --- | --- |
| 5fac706a7983a6d359b39acbd670e6800afe020e | Introduction of the 19 nonidentity business actions; route/wire acceptance |
| f32adc66af8d7ba66fc7b803a7f5291349c11932 | Credential validation; excluded Auth owner |
| 72eb9f1949176392141951d35e6e06f7c30af4c2 | Controller validation XML comments/package/generated documentation, no new business action |
| 3a393215d883fa35e1461f69c876bf2ead7ce36e | Deployment ingress/service split; excluded deployment |
| 0822636e5e2d46e4db20a79d27037aab426d85aa | Deployment resources/node selection; excluded |
| 3a104503328cc3c0d57ff9ae2deafba06d1e46d5 | Deployment node selection; excluded |
| 5458b7ddc81a15d72087fa69fb4cfcc27ae75747 | Frontend retirement/deployment; excluded |
| 53f4baf373ef04a3ed5ab5c1ef39bd61404c5258 | Deployment resource limits; excluded |
| 93f9f99522fbe6c128acb5d049f2b448e07dba95 | Deployment resource tuning; excluded |
| 90f34b389c298d1ce85abe2ae7ac92877dbbf7af | Frontend deployment scripts; excluded |
| 00ec830615c15b5e4e227046712247b11df0100f | Deployment hardening; excluded |
| 2aab25eb07894fc0267b03b85bad96490219d2fa | Database deployment externalization; excluded operational proof |
| 7d6f46f53cbab853ca9c25e385af067cfff6238a | Credential externalization; preserve current private configuration, no credentials/data actions |
| cbac7d7155da2208c77d56103b6a2cb19196fc83 | JWT externalization; ledger already migrated, normal RS256 controls here are not a redisposition |
| eb8ed86672bd9afccc6560b547b734d0fcd7363b | Secret-remediation merge cohort; excluded broad remediation/deployment |
| a649db99a27bda65274fe1b18866ae226d3c69cf | Merge remote-tracking origin/main; broad migration/credential history, not a distinct business action; excluded source-data migration |
| 03eaff1194c3ae2a54ceefeae31deffaff90436f | Docker context; excluded deployment |
| 72163e9ae11f39f6579423841a2e20529b986fab | Deployment status propagation; excluded |
| f8921b1b1d5846eeaff999af10b640011655d1d4 | Throwaway deployment rendering; excluded |
| 143f53ba0a1c81c78d252864ca131d42ed79dc1b | Production-secret readiness; excluded live operational gate |
| f0640fe0719b2eb6becda378bff08153d955be07 | Request failure tracing; already migrated ledger entry, retain existing behavior |
| 9e51e6c5da29de8e617b65b59d46882cde6d3b64 | Native logging transition; separate operational/PII review, no broad logging edits |
| 03dc9a1271c16e6535934445e9dd6e3f30e8fffe | Generated XML isolation; documentation build input, no business action |
| 5ac7d045c51194edd9e64d8564f1b726b001be34 | Application-local native logging; separate logging disposition |

## Genuine RED and proposed narrow correctness decision

`Cache_LatePreMutationReadCannotResurrectStaleProjectionInRealRedis` has four cases: employee update, narrow profile update, linked address update, employee DELETE. Real HTTP read obtains the old database projection and waits immediately before actual Redis SET. Another normal HTTP request commits its mutation and invalidates the cache; the held SET then publishes old bytes. A subsequent request returns old first name/address or 200 after deletion. The already-started first read may legitimately return its historical snapshot; only the subsequent request is asserted current. PostgreSQL assertions independently prove each mutation committed. The scheduler delegates every read/remove/set to the actual Redis adapter; no mocked repository or in-memory distributed guarantee.

Root approved PostgreSQL-authoritative ordinary detail reads, matching original committed `EmployeesController.GetEmployeeAsync` direct database ownership. Only `Application/Services/EmployeeApplicationService.cs:GetEmployeeAsync` changes; mutation invalidation keys and compatibility remain. No local/distributed lock, Redis-internal dependency or generation/schema migration is introduced. New regression scheduling now holds a real EF reader after the command result; the in-flight request may return old data, while subsequent requests must see fresh PostgreSQL. It additionally seeds the old projection into actual Redis after mutation to prove old-writer late fills cannot resurrect detail authority. Poisoned-cache and denied cache-read/removal controls use real Redis, with only fault transport scheduling controlled. This intentionally costs a database query per detail request; performance and old writers are not a certification claim.

The one old policy test now configures distinct stale cached and fresh repository projections; asserts fresh identity, repository once, and cache Get/Set never. Existing address/profile invalidation and pagination tests are retained unchanged. Historical scheduler/RED evidence remains in the recorded TRXs rather than being mislabeled current failures.

Documentation-only normal HTTP probe: Production `/employee/openapi/v1.json` correctly returns 404; nonProduction returns a real generated document with the expected business routes/query parameters and no identity routes. Historically `UpdateEmployeeSelfProfileRequest` properties were `firstName,lastName,phoneNumber,dateOfBirth`, while MVC had `PropertyNamingPolicy=null` and actual wire was PascalCase. Literal schema assertion RED was retained. Root-approved `Program.cs` local `ConfigureHttpJsonOptions` now aligns naming/dictionary/null options with unchanged MVC configuration. The actual HTTP test verifies exact property sets for all five requests, four responses and the paginated response, without reflected helper execution. Shared Defaults/production documentation exposure and business wire are unchanged.

## Evidence and remaining gates

Root independently inspected the complete six-file candidate and actual fault
evidence, then repeated Release build with warnings as errors: zero warnings,
zero errors. Focus63 and full164 passed with zero skips; whole format, all five
transitive vulnerability audits, test/document gitleaks and diff checks pass.
Root TRXs: `root-employee-final-focus/natth_MALIEV-31USFIV_2026-10-01_11_48_39_net10.0.trx`
and `root-employee-final-full/natth_MALIEV-31USFIV_2026-10-01_11_48_46_net10.0.trx`.
Generated outputs/private dependencies remain ignored and are not staged.
The raw API80 quality issue remains open; no threshold or denominator changed.

Private detached Defaults `8f4f5f27b226ffe406c4c79b1903742e8c2e7dd3`, Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7`; CI action `73dd7304ffe85ec504389fd7664cc39070b9f148`. Outputs remain private to this worktree.

Commands (PowerShell from owned workspace):

```powershell
dotnet build Legacy.Maliev.EmployeeService.slnx -c Release --no-restore -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-route-acceptance-20261001/.dependencies
dotnet test Legacy.Maliev.EmployeeService.Tests/Legacy.Maliev.EmployeeService.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~Cache_LatePreMutationRead --logger trx --results-directory TestResults/employee27-cache-race-red -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot=B:/maliev-legacy/.worktrees/employee-route-acceptance-20261001/.dependencies
```

Baseline TRX: `TestResults/employee27-baseline/natth_MALIEV-31USFIV_2026-10-01_11_10_08_net10.0.trx` (101 passed). Route TRX: `TestResults/employee27-route-controls/natth_MALIEV-31USFIV_2026-10-01_11_19_29_net10.0.trx` (56 passed). RED TRX: `TestResults/employee27-cache-race-red/natth_MALIEV-31USFIV_2026-10-01_11_32_49_net10.0.trx` (4 failed, 0 passed/skipped/errors). Initial accidental `.sln` command was an MSBuild path error, immediately corrected to actual `.slnx`; not product RED.

Raw unexcluded baseline coverage: API 13.89%, Application 48.38%, Data 84.27%. New route-control focus: API 27.49%, Application 95.96%, Data 92.13%. Final raw API 29.67%, Application 100%, Data 95.79%, Domain 100%. Generated OpenAPI XML-comment helpers (DocumentationCommentIdHelper, operation/schema transformers/caches) remain unexcluded and unhit even through the actual document route. `AddOpenApi` is called inside the referenced Defaults rather than directly in API Program; generated call-site interception is a likely explanation, not permission to execute private helpers or change registration just to inflate coverage. No reflection-only exercises, coverage exclusions, denominator changes or deployed-grant claims. Required raw API 80% remains open.

Final evidence paths:

- Focus `TestResults/employee27-verified-focus/natth_MALIEV-31USFIV_2026-10-01_11_44_34_net10.0.trx`: 63/63 passed.
- Full `TestResults/employee27-verified-full/natth_MALIEV-31USFIV_2026-10-01_11_44_54_net10.0.trx`: 164/164 passed.
- Coverage `TestResults/employee27-verified-full/e21e768c-1cc0-4534-94a3-3ac3735d71bc/coverage.cobertura.xml`.
- Documentation RED `TestResults/employee27-documentation-diagnostic/natth_MALIEV-31USFIV_2026-10-01_11_39_27_net10.0.trx`.
- Redacted secret report `TestResults/employee27-gitleaks.json`: zero findings, exit 0.

Final full command adds `--collect 'XPlat Code Coverage' --logger trx --results-directory TestResults/employee27-verified-full` to the test command above without its filter. Focus filter is `FullyQualifiedName~EmployeeRouteAcceptanceHttpTests`. Static commands: `dotnet format Legacy.Maliev.EmployeeService.slnx --verify-no-changes --no-restore`; `dotnet list Legacy.Maliev.EmployeeService.slnx package --vulnerable --include-transitive`; `gitleaks dir . --redact --log-level warn --report-format json --report-path TestResults/employee27-gitleaks.json`; `git diff --check`. Format/audit use `UseLocalMalievDependencies=true` and the exact private `MalievWorkspaceRoot` environment values. Scoped whitespace formatting touched only the two new test files after an initial formatting failure. All commands are terminal; outputs are released to root for independent review. No commit created.
