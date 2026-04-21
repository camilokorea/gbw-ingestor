DELETE FROM Observations WHERE Source = 'inat';
DELETE FROM Locations WHERE Id LIKE 'inat-%';
DELETE FROM IngestionQueue WHERE Source = 'inat';
