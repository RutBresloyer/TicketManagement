-- Sanity checks for the seeded demo dataset (read-only).
SET NOCOUNT ON;
USE RequestManagementDemo;

PRINT '--- Identity current values (next insert continues after the seeded IDs)';
SELECT IDENT_CURRENT('dbo.Requests') AS RequestsIdent, IDENT_CURRENT('dbo.RequestStatusHistory') AS HistoryIdent;

PRINT '--- Constraints must stay trusted after bulk copy (is_not_trusted = 0)';
SELECT name, is_not_trusted FROM sys.check_constraints
UNION ALL SELECT name, is_not_trusted FROM sys.foreign_keys;

PRINT '--- Distributions';
SELECT Status, COUNT(*) AS Rows FROM dbo.Requests GROUP BY Status ORDER BY Status;
SELECT Priority, COUNT(*) AS Rows FROM dbo.Requests GROUP BY Priority ORDER BY Priority;
SELECT COUNT(DISTINCT OrganizationName) AS Organizations, COUNT(DISTINCT AssignedTo) AS Assignees,
       SUM(CASE WHEN AssignedTo IS NULL THEN 1 ELSE 0 END) AS Unassigned,
       MIN(CreatedAt) AS MinCreatedAt, MAX(CreatedAt) AS MaxCreatedAt, MAX(UpdatedAt) AS MaxUpdatedAt
FROM dbo.Requests;

PRINT '--- Coherence: last history entry must match Status and UpdatedAt (expect 0)';
SELECT COUNT(*) AS Mismatches
FROM dbo.Requests r
CROSS APPLY (SELECT TOP (1) h.ChangedAt, h.NewStatus FROM dbo.RequestStatusHistory h
             WHERE h.RequestId = r.Id ORDER BY h.ChangedAt DESC, h.Id DESC) last
WHERE last.ChangedAt <> r.UpdatedAt OR last.NewStatus <> r.Status;

PRINT '--- Index definitions';
SELECT i.name AS IndexName,
       STRING_AGG(c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE '' END, ', ')
           WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
WHERE i.object_id IN (OBJECT_ID('dbo.Requests'), OBJECT_ID('dbo.RequestStatusHistory'))
GROUP BY i.name;
