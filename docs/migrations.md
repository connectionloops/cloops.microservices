# Database Migrations

Part of [Microsoft SQL Server](./mssql.md) in [Data Persistence](./data-persistence.md).

`cloops.microservices` can run SQL Server schema migrations at startup using [DbUp](https://github.com/DbUp/DbUp). The SDK looks for a `migrations` directory next to the app binary, coordinates execution with a NATS distributed lock, and applies any pending `.sql` scripts before caches, background jobs, and Kestrel start.

## Quick Start

Create a `migrations` folder in the consuming project:

```text
your-service/
  migrations/
    001.create.users.sql
    002.add.user.status.sql
```

Each migration is just a SQL file that DbUp runs once and records in its journal table:

```sql
CREATE TABLE dbo.users
(
    user_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_users PRIMARY KEY,
    email NVARCHAR(320) NOT NULL,
    display_name NVARCHAR(200) NULL,
    created_at DATETIMEOFFSET NOT NULL CONSTRAINT df_users_created_at DEFAULT SYSDATETIMEOFFSET()
);

CREATE UNIQUE INDEX ux_users_email ON dbo.users(email);
```

Make sure the folder is copied to the build output so it sits at the same level as the app binary in `bin`:

```xml
<ItemGroup>
  <Content Include="migrations/**/*.sql" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>

<!-- CopyToOutputDirectory never removes deleted source files from output. -->
<Target Name="SyncMigrationsToOutput" BeforeTargets="CopyFilesToOutputDirectory">
  <RemoveDir Directories="$(OutDir)migrations" Condition="Exists('$(OutDir)migrations')" />
</Target>
```

Configure the SQL Server connection string env variable in doppler. below is an example

```bash
CNSTR="Server=localhost;Database=mydb;User Id=sa;Password=..."
```

When the app starts, the SDK checks `AppContext.BaseDirectory/migrations`. If the directory exists and `ENABLE_MIGRATIONS` is not `False`, pending scripts are applied through DbUp.

## Migration Ordering

The effective order is based on script names, not file timestamps or SQL contents.

The SDK recursively loads every `*.sql` file under the output `migrations` directory and gives DbUp a script name based on the file path relative to that directory. Directory separators are normalized to `/`, so nested folders participate in the name sort:

```text
migrations/
  001.create.users.sql              -> 001.create.users.sql
  002.orders/001.create.orders.sql  -> 002.orders/001.create.orders.sql
```

DbUp then applies its upstream default ordering: scripts are sorted first by `RunGroupOrder`, then by script name using DbUp's configured `ScriptNameComparer`. The default comparer is ordinal and case-sensitive. This SDK uses `WithScripts(sqlScripts)` without custom `SqlScriptOptions`, run groups, or a custom script sorter, so all migrations are in the same run group and the practical order is lexicographic by relative script name.

Use zero-padded prefixes such as `001`, `002`, `010`, and avoid relying on case differences for ordering. DbUp does not perform natural numeric sorting, so `10.add.sql` sorts before `2.add.sql` unless the numbers are padded. DbUp journals the script name after it runs, and already-journaled scripts are skipped; adding a new lower-numbered file later only affects the order among pending scripts, not migrations that have already run.

## Startup Behavior

Hosted services start in registration order:

1. NATS lifecycle service connects first.
2. Database migrations wait briefly for NATS and acquire a distributed lock.
3. Cache services and user background services start.
4. Kestrel starts listening.

This prevents application work from running against a partially migrated schema. If migration SQL is executed and fails, startup fails.

If NATS is unavailable or the migration lock cannot be acquired, migrations are skipped and the SDK logs a clear warning. This usually means another pod may be applying migrations. For this reason, migrations should be backward compatible during rolling deploys.

## Configuration

| Variable            | Required                 | Default | Meaning                                                                     |
| ------------------- | ------------------------ | ------- | --------------------------------------------------------------------------- |
| `CNSTR`             | Yes, when migrations run | None    | SQL Server connection string used by DbUp.                                  |
| `ENABLE_MIGRATIONS` | No                       | `True`  | Set to `False` to skip migrations even when the `migrations` folder exists. |

No extra setting is needed to enable migrations. The presence of the output `migrations` directory is the convention.

## Best Practices

- Use forward-only, ordered filenames such as `001_create_table.sql`, `002_add_column.sql`.
- Do not edit scripts that may already have run in any shared environment. Add a new migration instead.
- Keep migrations backward compatible with the currently deployed application version. This matters during multi-pod rolling deploys and when one pod skips because another pod holds the lock.
- Prefer additive changes first: create nullable columns, deploy compatible code, backfill, then tighten constraints in a later migration.
- Keep destructive changes explicit and delayed until old app versions are gone.
- Test migrations against a disposable database before shipping.
- Keep scripts small enough that startup does not block for a long time.
- Remember that DbUp journals script names. Renaming an already-applied script makes it look new.
- Append new migrations (create new file) instead of inserting files into an already-shipped sequence.

## Upstream DbUp Docs

- [DbUp GitHub](https://github.com/DbUp/DbUp)
- [DbUp documentation](https://dbup.readthedocs.io/)
- [Script providers](https://dbup.readthedocs.io/en/latest/more-info/script-providers/)
- [Run group order](https://dbup.readthedocs.io/en/latest/more-info/run-group-order/)
- [Logging](https://dbup.readthedocs.io/en/latest/more-info/logging/)
- [Journaling](https://dbup.readthedocs.io/en/latest/more-info/journaling/)
