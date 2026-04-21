-- Migration: Add Tags column to FlickrPhotos
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[FlickrPhotos]') AND name = 'Tags')
BEGIN
    ALTER TABLE [dbo].[FlickrPhotos] ADD [Tags] NVARCHAR(MAX) NULL;
END
GO
