-- insert-napboxes.sql
-- Inserts NAP boxes into the PingDashboard SQLite database.
-- Idempotent: only inserts a name if it does not already exist (de-duplicated).
-- Target table: Napboxes(Id INTEGER PK AUTOINCREMENT, NapboxName TEXT NOT NULL)

BEGIN TRANSACTION;

INSERT INTO Napboxes (NapboxName)
SELECT name FROM (
    SELECT 'ALJOHN'     AS name UNION ALL
    SELECT 'ANDREI'     UNION ALL
    SELECT 'ARABELA'    UNION ALL
    SELECT 'BIMBO'      UNION ALL
    SELECT 'BRGYHALL1'  UNION ALL
    SELECT 'ROBERT'     UNION ALL
    SELECT 'CHICELLE'   UNION ALL
    SELECT 'COMPOUND'   UNION ALL
    SELECT 'FLORANTE'   UNION ALL
    SELECT 'IGLESIA'    UNION ALL
    SELECT 'ITLUGAN'    UNION ALL
    SELECT 'JACKYBAYAN' UNION ALL
    SELECT 'JHAY'       UNION ALL
    SELECT 'OGIE'       UNION ALL
    SELECT 'EXTENSION'  UNION ALL
    SELECT 'SAPA1'      UNION ALL
    SELECT 'SAPA2'      UNION ALL
    SELECT 'SWEETIE'
) AS incoming
WHERE NOT EXISTS (
    SELECT 1 FROM Napboxes x WHERE x.NapboxName = incoming.name
);

COMMIT;

-- Show the current list after insert
SELECT Id, NapboxName FROM Napboxes ORDER BY NapboxName;
