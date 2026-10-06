# Conditional employee home-address edits

Intranet administrative editing must fence the selected employee-to-address binding as well as the address contents. A separate profile preflight followed by the standalone address writer cannot fence a concurrent relation move.

Read `GET /employees/{employeeId}/edit` with the existing employee-read permission and `GET /employees/addresses/{addressId}` with the existing address-read permission. Preserve the two distinct strong ETags captured with the edit form and derive the address ID from that employee's `HomeAddressId`; never accept arbitrary browser relation assignments.

Send `PUT /employees/{employeeId}/home-address/versioned` with:

- `If-Match`: captured address ETag.
- `X-Employee-If-Match`: captured employee ETag.
- PascalCase body: `AddressId`, `Building`, `AddressLine1`, `AddressLine2`, `City`, `State`, `PostalCode`, `CountryId`.

Unknown JSON properties and nonpositive `AddressId` are refused. Other address field semantics remain those of the ordinary address writer. Both employee-update permission on `/employees/{employeeId}` and address-update permission on `/employees/addresses/{AddressId}` are required through the existing authorization handler; no new grants are introduced.

The producer locks the employee row and then the captured address row in one PostgreSQL transaction. It compares the employee version, actual binding and address version under these locks before changing only address contents. Ordinary employee/address updates also acquire database row locks and therefore serialize against these fences. Shared address semantics remain: other employees bound to that same row see the edit, and their cached projections are invalidated.

Success is `204` with the new address `ETag` and unchanged employee version in `X-Employee-ETag`. Missing either version returns `428`; malformed/weak/wildcard/multiple versions return `400`; stale versions or changed/absent binding return `412`; an absent employee returns `404`. A commit acknowledgment failure returns a generic `500`, can represent an already committed write, and must trigger a refresh without automatic replay or assuming rollback. Caller cancellation remains cancellation, with independently attempted cache cleanup for potentially affected projections. Cache outages cannot guarantee invalidation; ordinary reads remain authoritative database reads.

The employee profile version excludes independent address contents and remains unchanged by this endpoint. Reuse the success `X-Employee-ETag` for a later conditional profile write; that later write can still fail if another writer changed the profile after this transaction. There is no distributed transaction with Auth or later profile writes, no durable operation receipt, and no monotonic ABA protection. Ordinary routes, schema, permissions, dependency pins and identity ownership are unchanged.

Regressions use the normal Production RS256 permission pipeline with controlled IAM transport, PostgreSQL 18 and Redis. The two relation races schedule before the employee fence (move commits first, stale address edit changes nothing) and after the fence (ordinary move is observed waiting on a real PostgreSQL lock until the address commit). Separate cases cover both required permissions, captured-ID mismatch, strong-version refusal, shared cache cleanup, commit acknowledgment loss and caller cancellation. This component evidence does not prove deployed IAM grants or the Intranet consumer integration.
