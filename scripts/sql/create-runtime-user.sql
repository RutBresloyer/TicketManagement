-- Least-privilege runtime principal for the API (run AFTER migrations; idempotent).
-- The password is read from the RM_APP_PASSWORD environment variable (sqlcmd exposes environment variables as
-- scripting variables), so it never appears in this file or on a command line.
--
-- Permissions are exactly what the API needs:
--   * SELECT on both tables (all GET endpoints, the WHERE/OUTPUT clauses of the update).
--   * UPDATE on Requests(Status, UpdatedAt) only — the only columns the API changes.
--   * INSERT on RequestStatusHistory — audit rows can be appended but NEVER updated or deleted by the application.
-- No DDL, no DELETE, no access to __EFMigrationsHistory or DemoSeedInfo.

USE [master];
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'rm_app')
    CREATE LOGIN [rm_app] WITH PASSWORD = N'$(RM_APP_PASSWORD)', CHECK_POLICY = ON, DEFAULT_DATABASE = [RequestManagementDemo];
ELSE
    ALTER LOGIN [rm_app] WITH PASSWORD = N'$(RM_APP_PASSWORD)';
GO

USE [RequestManagementDemo];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'rm_app')
    CREATE USER [rm_app] FOR LOGIN [rm_app];
GO

GRANT SELECT ON OBJECT::dbo.Requests TO [rm_app];
GRANT UPDATE ON OBJECT::dbo.Requests ([Status], [UpdatedAt]) TO [rm_app];
GRANT SELECT, INSERT ON OBJECT::dbo.RequestStatusHistory TO [rm_app];
GO

PRINT 'Runtime user rm_app is configured.';
