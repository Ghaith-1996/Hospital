# Database restore test

## Purpose
Verify a real PostgreSQL custom-format backup can restore into a separate empty database and pass application-level structural/relational validation.

## Scope
The script creates an isolated, pinned PostgreSQL container with a seeded fictional source. It never selects an existing developer/hospital database. Only the fixed source name is accepted; the restore name is unique. Development/Test and a local Docker engine with a loopback-bound port are required.

## Detection
Any failed safety guard, native command, schema/count/integrity check or cleanup flag fails the exercise. Missing explicit environment or confirmation fails before creating resources.

## Safety impact
No source restore/overwrite occurs. The source is private to the exercise and has no concurrent worker writes. All EF-model tables, including Phase 9 escalation tables, are counted. Empty workflow tables remain empty in this seeded-source exercise; connected system tests independently verify populated workflows. This is not a production disaster-recovery test.

## Immediate actions
From the repository root with pinned tools and Docker available:
```powershell
$env:ASPNETCORE_ENVIRONMENT = "Test"
./scripts/db-restore-test.ps1 -ConfirmRestoreTest
./scripts/test-db-restore-safety.ps1 -ExerciseCleanup
```
No connection string, key or password is passed on a command line. The script generates disposable credentials in the process environment and restores prior environment values afterward.

## What NOT to do
Do not restore over the source, target an existing database, supply Staging/Production/unknown environment, relax safety guards, retain dumps, disable audit protection or infer production RPO/RTO from elapsed time.

## Diagnosis
Use only RESTORE_TEST flags and exit status. Native output is suppressed to avoid secret/provider-error disclosure. The database validate-restore host command reads environment configuration, starts a read-only repeatable-read transaction, verifies exact migration history/table availability, foreign-key and organization relationships, validated constraints and enabled append-only trigger. It returns counts/schema names only.

## Recovery
Correct local tool/container availability and rerun from a clean state. pg_dump writes inside the owned container; createdb creates an empty unique target; pg_restore restores schema/data/constraints. An attempted no-row audit UPDATE proves restored append-only enforcement. No migration or workflow write is performed by the validator. Failures after backup and after restore are injectable in Test and must clean up.

## Verification
Require source_database_validated, backup_created, temporary_database_created, restore_succeeded, audit_append_only_verified, schema_verified, relational_invariants_matched, safe_counts_matched, restored_database_readable and source_unchanged. Require temporary_database_removed, temporary_dump_removed and owned_container_removed. A failed cleanup is a failed exercise even when restore succeeded.

## Evidence to preserve
Commit/tool versions, exit status, the safe flag summary and measured backup/restore/verification duration. No dump, database rows, connection string, protection key or native error output. The script drops the temporary database, deletes its in-container dump, and removes the owned container/anonymous volume on success and injected failures.

## Exit criteria
Real backup/restore passes, invariants match, source is unchanged and all owned resources are removed. Both injected failure cleanup paths pass. Measured simulation exercise duration is not an approved recovery objective.

## Production decisions
REQUIRES_HOSPITAL_DECISION: database RPO, database RTO, backup retention, recovery authority, encrypted backup/key custody, disaster recovery environment, support/on-call contact, legal hold and production recovery approval.
