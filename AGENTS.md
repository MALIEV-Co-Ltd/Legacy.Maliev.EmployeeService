# Legacy.Maliev.EmployeeService agent guidance

## Boundaries

- Preserve the 24 approved employee, address, role, signature, and identity routes,
  query names, sort values, PascalCase JSON, null omission, and named routes.
- EmployeeService owns `Employee`, `Address`, `Role`, and `SignatureImageFile` only.
  `EmployeeIdentity`, credentials, tokens, sessions, and recovery data belong to
  AuthService and must never be added to this repository or database.
- Keep Google Cloud Storage metadata only. Use ADC/Workload Identity at runtime;
  never add service-account files, access keys, signed URLs, or credentials.
- Do not alter source SQL Server or deploy during extraction. PostgreSQL promotion
  requires artifact-backed parity, rollback, and consumer gates.

## Runtime constraints

- .NET 10, Scalar/OpenAPI, Npgsql, Redis, built-in `ILogger<T>`, and standard
  MALIEV service defaults are required.
- Run only in the existing GKE cluster, namespace `maliev-legacy`; no new node pool,
  Cloud SQL, or other paid infrastructure.
- Keep logs at Warning by default and never log employee PII, credentials, tokens,
  request bodies, signature object URLs, or headers.
- Treat all staff routes as authenticated and permission-protected. Credential and
  identity mutations require live checks; destructive operations are critical.

## Validation and commits

- Cross-boundary changes require route/DTO tests plus PostgreSQL 18 integration tests.
- Run build, tests, format verification, package vulnerability audit, and gitleaks.
- Commit coherent validated slices; do not mix unrelated changes.
