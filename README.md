# Legacy.Maliev.EmployeeService

Public, sanitized .NET 10 compatibility extraction of the employee domain from the
private `maliev-web` monorepo. It preserves employee, address, role, signature
metadata, pagination, sort, and safe identity DTO contracts while replacement
services are completed independently.

## Architecture and security boundaries

Dependency direction is `Api -> Application -> Domain`; PostgreSQL, Redis, and
AuthService HTTP adapters live in `Data`. Scalar/OpenAPI, JWT validation, standard
middleware, health endpoints, resilience, and structured logging come from
`Maliev.Aspire.ServiceDefaults`.

EmployeeService no longer opens or migrates `EmployeeIdentity`. Password hashes,
security stamps, authenticator keys, recovery material, sessions, refresh tokens,
and credential validation remain AuthService-owned. The old anonymous credential
oracle is protected by a live-checked critical staff permission and returns one
non-enumerating unauthorized result.

## Preserved route families

- `/Employees[/{id}]` and `/Employees?sort=&search=&index=&size=`
- `/Employees/{id}/identity[/{password?}]`
- `/employees/addresses[/{addressId}]`
- `/employees/roles[/{roleId}]`
- `/employees/signatures[/{employeeId}]`
- `/employees/{employeeId}/signatures`
- `/Employees/v1/validate`

PascalCase JSON, null omission, the four legacy sort values, named routes and the
pagination fields `Items`, `PageIndex`, `TotalPages`, `TotalRecords`,
`HasNextPage`, and `HasPreviousPage` remain compatible. List size is bounded to
250 records to protect the existing cluster.

## Data, signatures and caching

- PostgreSQL target: `legacy-postgres-employee` in `maliev-legacy` after parity gates.
- Preserved tables: `Employee`, `Address`, `Role`, `SignatureImageFile`, including
  legacy `ID`/foreign-key names, lengths, computed `FullName`, and FK names.
- `EmployeeIdentity` remains in AuthService and source SQL Server is untouched.
- Signature rows contain Google Cloud Storage bucket/object metadata only. Runtime
  object access must use ADC/Workload Identity; no service-account keys are stored.
- Redis prefix: `legacy:employee:`; authorized employee projections are short-lived,
  fail open to PostgreSQL, and are invalidated after employee/address/role changes.

## Deployment gate

Extraction does not deploy. Cutover requires a dedicated `legacy-maliev-employee`
WIF identity, `maliev-gitops/3-apps/_legacy-employee-service`, the AuthService
employee-identity adapter, data parity/rollback evidence, signature-object
reconciliation, and Web/Intranet consumer tests. The legacy password-in-route
caller must move to a body-only endpoint before production cutover.

Everything must use the existing GKE cluster and `maliev-legacy` namespace with no
new node pool, Cloud SQL, or other paid database service.

## Validate

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
dotnet format Legacy.Maliev.EmployeeService.slnx --verify-no-changes --no-restore
dotnet list package --vulnerable --include-transitive
gitleaks git . --redact=100 --exit-code 1 --no-banner --no-color
```
