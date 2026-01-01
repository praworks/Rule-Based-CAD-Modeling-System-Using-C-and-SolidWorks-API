MongoMigrate
===============

Small console tool to merge existing `Feedback` / `feedback2` collections into `run_feedback`.

Usage:

```powershell
cd scripts/MongoMigrate
dotnet restore
dotnet run -- "<MONGO_URI>" [TaskPaneAddin] [--dry-run]
```

Examples:

```powershell
dotnet run -- "mongodb+srv://user:pass@cluster.example.net" TaskPaneAddin --dry-run
dotnet run -- "mongodb://localhost:27017" TaskPaneAddin
```

The tool is idempotent: it upserts into `run_feedback` using a composite key of `run_key`,`ts`,`thumb`,`comment`.
It also creates recommended indexes after migration.

Always take a `mongodump` backup before running in non-dry-run mode.
