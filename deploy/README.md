# EmployeeService cutover contract

These files are dormant delivery inputs. They do not create a cluster, database,
secret, workload-identity binding, or GitOps application, and the publish workflow
does not apply them. Cutover is allowed only in the existing `maliev-legacy`
namespace after the Project #2 parity and owner gates pass.

## Runtime secret

GitOps must project an existing secret named `legacy-maliev-employee-runtime` with
exactly the service's runtime bindings:

- `ConnectionStrings__EmployeeDbContext`
- `ConnectionStrings__redis`
- `Jwt__PublicKey`
- `Jwt__Issuer`
- `Jwt__Audience`

The JWT value is verification-only public key material. Employee identity,
credentials, refresh tokens, sessions, recovery data, signing keys, and service
account key files are forbidden. The Kubernetes service account must use the
owner-approved Workload Identity binding; no credential file may be mounted.

## Promotion prerequisites

- replace the zero image digest in `base/kustomization.yaml` with the immutable
  digest produced by exact-commit CI;
- confirm the existing cluster and namespace have measured capacity for the stated
  requests and limits, without adding a node pool or paid database;
- complete PostgreSQL row-count, key, nullability, timestamp, and signature-object
  metadata parity and preserve the evidence artifact;
- pass authenticated Web and Intranet consumer tests against the staging service;
- verify AuthService remains the only employee identity owner and no identity table,
  proxy route, or credential configuration exists here;
- read back the Secret Manager to Kubernetes projection with values redacted, and
  verify the WIF principal and least-privilege bindings;
- obtain explicit owner approval for migration, traffic promotion, and rollback.

## Rollback prerequisites

Before promotion, record the previous immutable image digest and GitOps revision,
capture a recoverable snapshot of the existing PostgreSQL employee database, and
retain the source SQL Server unchanged and available under the approved read-only
rollback policy. Preserve the pre-cutover consumer routing state and the parity
artifact. A rollback must re-pin the reviewed prior digest and restore consumer
routing through GitOps; do not run a destructive down migration or delete promoted
data without separate owner approval and verified recovery evidence.
