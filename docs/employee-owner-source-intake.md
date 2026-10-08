# Employee owner-boundary raw source intake

`Employee sealed source intake` supplies the missing raw-source transport step for the frozen owner-boundary qualification packet. It reuses the accepted `sealed_source_capsule.py` decoder byte-for-byte (SHA256 `44a8a5accac9da11422d606be02fe28487642215df511b5f1c4284296a453ee2`) from the Customer producer accepted at `10b9a24bc07bf91e2417310bbc9723558583501b`. It does not reuse Customer execution authority, provider policy or runtime acceptance.

The Employee-specific bootstrap policy binds the exact repository, accepted base `bb7333e8e6153b19de394fb3c45925c6dc948934`, three immutable Git blob objects, ZIP headers, raw hashes, byte counts, candidate review seals and dependency commits. Each capsule is stored under `scripts/employee-source-packet`; the workflow fetches its exact object from this repository with a read-only token and the accepted bounded decoder. Arbitrary URLs, caller repositories, runtime commands and native grants are absent from the interface.

The source-only entry point is `.github/workflows/employee-source-intake.yml`, triggered by scoped transport PR changes or a manual dispatch with no inputs. It validates 18 pure source controls, materializes the raw projections under `$RUNNER_TEMP/employee-owner-source`, retains its source receipt and releases only the source tree whose marker matches this run ID and attempt. It never runs .NET, starts backends or deploys. Source receipt fields explicitly report zero runtime tests and no native execution grant.

The fixed materialized layout is:

| Directory | Frozen files | Purpose |
|---|---:|---|
| `baseline` | 103 | Original 102 committed files plus the reviewed PostgreSQL ownership observer test |
| `candidate` | 103 | Original base plus the exact four-file reviewed security adaptation |
| `dependencies` | 151 | Defaults `c40a7f82cea347b949444dcd7fb730f2b8dc3c0e` and Contracts `78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7` |

The projections are byte-preserving copies. Canonical migration-file CRLF differences do not enter the capsules; all 102 base files came from raw committed Git bytes. The transport doesn't normalize source or replace the current application checkout. The dependency upgrade in PR53 remains a separate validated change; the frozen qualification views continue to contain the originally accepted bb733 workflow and contract test together.

The qualification consumer must use the sealed `baseline` and `candidate` directories as separate working directories and set `MalievWorkspaceRoot` to this layout's `dependencies` directory. The source policy is not a runtime grant. Before any native phase, the consumer must separately check current finite allocation authority, actual predecessor phase retirement, fresh >=4194304 KiB physical memory, no competing native runner, exact source/dependency seals and owned process/backend cleanup. The historical October4 allocation remains released.

The required runtime sequence is baseline restore and strict zero-warning/error Release build, 12 focused real-HTTP/PostgreSQL observer cases, reviewed actual mismatch RED, candidate strict build, focused12 plus three explicitly adapted cases, full371 forecast, formatting/audit/security/scaffold checks and generated-inclusive raw >=80 percent coverage for all four production assemblies with no exclusions. Setup/compiler/dependency/backend/timeout failures do not constitute behavioral RED. Source-control success is not native acceptance. Preserve full original tests and nested Auth proof; obtain current-head and fresh-main checks before accepting a future business commit.

This transport publication does not apply the four-file C# candidate to production source. Owner-boundary baseline RED and all candidate runtime gates remain unrun until a separately admitted qualification consumer executes them.
