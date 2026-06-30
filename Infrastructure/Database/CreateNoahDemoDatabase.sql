IF DB_ID(N'NOAH_Demo') IS NULL
BEGIN
    CREATE DATABASE [NOAH_Demo];
END;
GO

USE [NOAH_Demo];
GO

IF OBJECT_ID(N'dbo.DemoUsers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DemoUsers
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_DemoUsers PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Username NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(150) NOT NULL,
        PasswordSalt NVARCHAR(200) NOT NULL,
        PasswordHash NVARCHAR(200) NOT NULL,
        PasswordIterations INT NOT NULL CONSTRAINT DF_DemoUsers_PasswordIterations DEFAULT 100000,
        IsEnabled BIT NOT NULL CONSTRAINT DF_DemoUsers_IsEnabled DEFAULT 1,
        LastSignedInAtUtc DATETIMEOFFSET(7) NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_DemoUsers_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_DemoUsers_PasswordIterations CHECK (PasswordIterations BETWEEN 10000 AND 600000)
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DemoUsers_Username' AND object_id = OBJECT_ID(N'dbo.DemoUsers'))
    CREATE UNIQUE INDEX IX_DemoUsers_Username ON dbo.DemoUsers(Username);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.DemoUsers WHERE Username = N'noah-school')
BEGIN
    INSERT INTO dbo.DemoUsers
    (
        Username,
        DisplayName,
        PasswordSalt,
        PasswordHash,
        PasswordIterations,
        IsEnabled
    )
    VALUES
    (
        N'noah-school',
        N'NOAH School Access',
        N'MuLpjkDqCKi7OX2PgyQO+A==',
        N'8u88oqTre26zWq/vn5PYbHs3aLN0G6kuUfXVqzkiyQk=',
        100000,
        1
    );
END;
GO

IF OBJECT_ID(N'dbo.AssistantInteractions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AssistantInteractions
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AssistantInteractions PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        ChatId UNIQUEIDENTIFIER NULL,
        UserInput NVARCHAR(MAX) NOT NULL,
        InputMode INT NOT NULL CONSTRAINT DF_AssistantInteractions_InputMode DEFAULT 0,
        ActionType INT NOT NULL CONSTRAINT DF_AssistantInteractions_ActionType DEFAULT 0,
        AssistantResponse NVARCHAR(MAX) NULL,
        ResponseMode INT NOT NULL CONSTRAINT DF_AssistantInteractions_ResponseMode DEFAULT 0,
        Status INT NOT NULL CONSTRAINT DF_AssistantInteractions_Status DEFAULT 0,
        RelatedEntityId UNIQUEIDENTIFIER NULL,
        RelatedEntityType NVARCHAR(100) NULL,
        ErrorMessage NVARCHAR(1000) NULL,
        RequestedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AssistantInteractions_RequestedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        CompletedAtUtc DATETIMEOFFSET(7) NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AssistantInteractions_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_AssistantInteractions_InputMode CHECK (InputMode IN (0, 1)),
        CONSTRAINT CK_AssistantInteractions_ActionType CHECK (ActionType BETWEEN 0 AND 13),
        CONSTRAINT CK_AssistantInteractions_ResponseMode CHECK (ResponseMode IN (0, 1, 2)),
        CONSTRAINT CK_AssistantInteractions_Status CHECK (Status IN (0, 1, 2, 3))
    );
END;
GO

IF COL_LENGTH(N'dbo.AssistantInteractions', N'ChatId') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantInteractions
        ADD ChatId UNIQUEIDENTIFIER NULL;
END;
GO

IF OBJECT_ID(N'dbo.CK_AssistantInteractions_ActionType', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.AssistantInteractions
        DROP CONSTRAINT CK_AssistantInteractions_ActionType;
END;
GO

ALTER TABLE dbo.AssistantInteractions WITH CHECK
    ADD CONSTRAINT CK_AssistantInteractions_ActionType CHECK (ActionType BETWEEN 0 AND 13);
GO

IF OBJECT_ID(N'dbo.AssistantSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AssistantSettings
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AssistantSettings PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        PreferredResponseMode INT NOT NULL CONSTRAINT DF_AssistantSettings_PreferredResponseMode DEFAULT 0,
        SpeechCulture NVARCHAR(20) NOT NULL CONSTRAINT DF_AssistantSettings_SpeechCulture DEFAULT N'en-US',
        EnableChatMemory BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableChatMemory DEFAULT 1,
        EnableLongTermMemory BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableLongTermMemory DEFAULT 1,
        EnableMemoryCapture BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableMemoryCapture DEFAULT 1,
        ConversationMemoryMessageCount INT NOT NULL CONSTRAINT DF_AssistantSettings_ConversationMemoryMessageCount DEFAULT 6,
        LongTermMemoryItemCount INT NOT NULL CONSTRAINT DF_AssistantSettings_LongTermMemoryItemCount DEFAULT 6,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AssistantSettings_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_AssistantSettings_PreferredResponseMode CHECK (PreferredResponseMode IN (0, 1, 2)),
        CONSTRAINT CK_AssistantSettings_ConversationMemoryMessageCount CHECK (ConversationMemoryMessageCount BETWEEN 0 AND 20),
        CONSTRAINT CK_AssistantSettings_LongTermMemoryItemCount CHECK (LongTermMemoryItemCount BETWEEN 0 AND 20)
    );
END;
GO

IF COL_LENGTH(N'dbo.AssistantSettings', N'EnableChatMemory') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings
        ADD EnableChatMemory BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableChatMemory DEFAULT 1;
END;
GO

IF COL_LENGTH(N'dbo.AssistantSettings', N'EnableLongTermMemory') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings
        ADD EnableLongTermMemory BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableLongTermMemory DEFAULT 1;
END;
GO

IF COL_LENGTH(N'dbo.AssistantSettings', N'EnableMemoryCapture') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings
        ADD EnableMemoryCapture BIT NOT NULL CONSTRAINT DF_AssistantSettings_EnableMemoryCapture DEFAULT 1;
END;
GO

IF COL_LENGTH(N'dbo.AssistantSettings', N'ConversationMemoryMessageCount') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings
        ADD ConversationMemoryMessageCount INT NOT NULL CONSTRAINT DF_AssistantSettings_ConversationMemoryMessageCount DEFAULT 6;
END;
GO

IF COL_LENGTH(N'dbo.AssistantSettings', N'LongTermMemoryItemCount') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings
        ADD LongTermMemoryItemCount INT NOT NULL CONSTRAINT DF_AssistantSettings_LongTermMemoryItemCount DEFAULT 6;
END;
GO

IF OBJECT_ID(N'dbo.CK_AssistantSettings_ConversationMemoryMessageCount', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings WITH CHECK
        ADD CONSTRAINT CK_AssistantSettings_ConversationMemoryMessageCount CHECK (ConversationMemoryMessageCount BETWEEN 0 AND 20);
END;
GO

IF OBJECT_ID(N'dbo.CK_AssistantSettings_LongTermMemoryItemCount', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantSettings WITH CHECK
        ADD CONSTRAINT CK_AssistantSettings_LongTermMemoryItemCount CHECK (LongTermMemoryItemCount BETWEEN 0 AND 20);
END;
GO

IF OBJECT_ID(N'dbo.AssistantChats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AssistantChats
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AssistantChats PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Title NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL,
        IsArchived BIT NOT NULL CONSTRAINT DF_AssistantChats_IsArchived DEFAULT 0,
        LastMessagePreview NVARCHAR(300) NULL,
        LastMessageAtUtc DATETIMEOFFSET(7) NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AssistantChats_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.AssistantMemoryItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AssistantMemoryItems
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AssistantMemoryItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(4000) NOT NULL,
        Tags NVARCHAR(500) NULL,
        IsPinned BIT NOT NULL CONSTRAINT DF_AssistantMemoryItems_IsPinned DEFAULT 0,
        SourceInteractionId UNIQUEIDENTIFIER NULL,
        SourceChatId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_AssistantMemoryItems_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT FK_AssistantMemoryItems_AssistantInteractions_SourceInteractionId
            FOREIGN KEY (SourceInteractionId) REFERENCES dbo.AssistantInteractions(Id)
            ON DELETE SET NULL,
        CONSTRAINT FK_AssistantMemoryItems_AssistantChats_SourceChatId
            FOREIGN KEY (SourceChatId) REFERENCES dbo.AssistantChats(Id)
            ON DELETE SET NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.TaskItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TaskItems
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TaskItems PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Title NVARCHAR(200) NOT NULL,
        Description NVARCHAR(2000) NULL,
        Status INT NOT NULL CONSTRAINT DF_TaskItems_Status DEFAULT 0,
        Priority INT NOT NULL CONSTRAINT DF_TaskItems_Priority DEFAULT 1,
        DueAtUtc DATETIMEOFFSET(7) NULL,
        PlannedFor DATE NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_TaskItems_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_TaskItems_Status CHECK (Status IN (0, 1, 2, 3)),
        CONSTRAINT CK_TaskItems_Priority CHECK (Priority IN (0, 1, 2))
    );
END;
GO

IF OBJECT_ID(N'dbo.Notes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Notes
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Notes PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Title NVARCHAR(200) NOT NULL,
        Content NVARCHAR(MAX) NOT NULL,
        CapturedFromVoice BIT NOT NULL CONSTRAINT DF_Notes_CapturedFromVoice DEFAULT 0,
        SourceInteractionId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_Notes_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT FK_Notes_AssistantInteractions_SourceInteractionId
            FOREIGN KEY (SourceInteractionId) REFERENCES dbo.AssistantInteractions(Id)
            ON DELETE SET NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.SavedLocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SavedLocations
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SavedLocations PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Name NVARCHAR(200) NOT NULL,
        Latitude DECIMAL(9, 6) NOT NULL,
        Longitude DECIMAL(9, 6) NOT NULL,
        AccuracyMeters DECIMAL(10, 2) NULL,
        Address NVARCHAR(500) NULL,
        CreatedFromCurrentLocation BIT NOT NULL CONSTRAINT DF_SavedLocations_CreatedFromCurrentLocation DEFAULT 0,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_SavedLocations_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_SavedLocations_Latitude CHECK (Latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_SavedLocations_Longitude CHECK (Longitude BETWEEN -180 AND 180),
        CONSTRAINT CK_SavedLocations_AccuracyMeters CHECK (AccuracyMeters IS NULL OR AccuracyMeters >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.Reminders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Reminders
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Reminders PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Title NVARCHAR(200) NOT NULL,
        Message NVARCHAR(2000) NULL,
        TriggerType INT NOT NULL CONSTRAINT DF_Reminders_TriggerType DEFAULT 0,
        Status INT NOT NULL CONSTRAINT DF_Reminders_Status DEFAULT 0,
        ShouldNotify BIT NOT NULL CONSTRAINT DF_Reminders_ShouldNotify DEFAULT 1,
        TriggerAtUtc DATETIMEOFFSET(7) NULL,
        TriggerLatitude DECIMAL(9, 6) NULL,
        TriggerLongitude DECIMAL(9, 6) NULL,
        TriggerAccuracyMeters DECIMAL(10, 2) NULL,
        TriggerRadiusMeters DECIMAL(10, 2) NULL,
        LastTriggeredAtUtc DATETIMEOFFSET(7) NULL,
        TaskItemId UNIQUEIDENTIFIER NULL,
        NoteId UNIQUEIDENTIFIER NULL,
        SavedLocationId UNIQUEIDENTIFIER NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_Reminders_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_Reminders_TriggerType CHECK (TriggerType IN (0, 1)),
        CONSTRAINT CK_Reminders_Status CHECK (Status IN (0, 1, 2, 3)),
        CONSTRAINT CK_Reminders_TriggerLatitude CHECK (TriggerLatitude IS NULL OR TriggerLatitude BETWEEN -90 AND 90),
        CONSTRAINT CK_Reminders_TriggerLongitude CHECK (TriggerLongitude IS NULL OR TriggerLongitude BETWEEN -180 AND 180),
        CONSTRAINT CK_Reminders_TriggerAccuracyMeters CHECK (TriggerAccuracyMeters IS NULL OR TriggerAccuracyMeters >= 0),
        CONSTRAINT CK_Reminders_TriggerRadiusMeters CHECK (TriggerRadiusMeters IS NULL OR TriggerRadiusMeters >= 0),
        CONSTRAINT CK_Reminders_TimeTrigger CHECK (TriggerType <> 0 OR TriggerAtUtc IS NOT NULL),
        CONSTRAINT CK_Reminders_LocationTrigger CHECK (TriggerType <> 1 OR (TriggerLatitude IS NOT NULL AND TriggerLongitude IS NOT NULL)),

        CONSTRAINT FK_Reminders_TaskItems_TaskItemId
            FOREIGN KEY (TaskItemId) REFERENCES dbo.TaskItems(Id)
            ON DELETE SET NULL,
        CONSTRAINT FK_Reminders_Notes_NoteId
            FOREIGN KEY (NoteId) REFERENCES dbo.Notes(Id)
            ON DELETE SET NULL,
        CONSTRAINT FK_Reminders_SavedLocations_SavedLocationId
            FOREIGN KEY (SavedLocationId) REFERENCES dbo.SavedLocations(Id)
            ON DELETE SET NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.MileageEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.MileageEntries
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MileageEntries PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        RecordedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MileageEntries_RecordedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        OdometerReadingKm DECIMAL(18, 2) NOT NULL,
        TripDistanceKm DECIMAL(18, 2) NULL,
        Source INT NOT NULL CONSTRAINT DF_MileageEntries_Source DEFAULT 0,
        SourceImagePath NVARCHAR(1024) NULL,
        RecognizedText NVARCHAR(MAX) NULL,
        CorrectedText NVARCHAR(MAX) NULL,
        IsTextCorrected BIT NOT NULL CONSTRAINT DF_MileageEntries_IsTextCorrected DEFAULT 0,
        Latitude DECIMAL(9, 6) NULL,
        Longitude DECIMAL(9, 6) NULL,
        AccuracyMeters DECIMAL(10, 2) NULL,
        Notes NVARCHAR(2000) NULL,
        CreatedAtUtc DATETIMEOFFSET(7) NOT NULL CONSTRAINT DF_MileageEntries_CreatedAtUtc DEFAULT TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        UpdatedAtUtc DATETIMEOFFSET(7) NULL,

        CONSTRAINT CK_MileageEntries_OdometerReadingKm CHECK (OdometerReadingKm >= 0),
        CONSTRAINT CK_MileageEntries_TripDistanceKm CHECK (TripDistanceKm IS NULL OR TripDistanceKm >= 0),
        CONSTRAINT CK_MileageEntries_Source CHECK (Source IN (0, 1, 2)),
        CONSTRAINT CK_MileageEntries_Latitude CHECK (Latitude IS NULL OR Latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_MileageEntries_Longitude CHECK (Longitude IS NULL OR Longitude BETWEEN -180 AND 180),
        CONSTRAINT CK_MileageEntries_AccuracyMeters CHECK (AccuracyMeters IS NULL OR AccuracyMeters >= 0)
    );
END;
GO

IF OBJECT_ID(N'dbo.FK_AssistantInteractions_AssistantChats_ChatId', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantInteractions
        ADD CONSTRAINT FK_AssistantInteractions_AssistantChats_ChatId
            FOREIGN KEY (ChatId) REFERENCES dbo.AssistantChats(Id)
            ON DELETE SET NULL;
END;
GO

IF OBJECT_ID(N'dbo.FK_AssistantMemoryItems_AssistantInteractions_SourceInteractionId', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantMemoryItems
        ADD CONSTRAINT FK_AssistantMemoryItems_AssistantInteractions_SourceInteractionId
            FOREIGN KEY (SourceInteractionId) REFERENCES dbo.AssistantInteractions(Id)
            ON DELETE SET NULL;
END;
GO

IF OBJECT_ID(N'dbo.FK_AssistantMemoryItems_AssistantChats_SourceChatId', N'F') IS NULL
BEGIN
    ALTER TABLE dbo.AssistantMemoryItems
        ADD CONSTRAINT FK_AssistantMemoryItems_AssistantChats_SourceChatId
            FOREIGN KEY (SourceChatId) REFERENCES dbo.AssistantChats(Id)
            ON DELETE SET NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssistantInteractions_RequestedAtUtc' AND object_id = OBJECT_ID(N'dbo.AssistantInteractions'))
    CREATE INDEX IX_AssistantInteractions_RequestedAtUtc ON dbo.AssistantInteractions(RequestedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssistantInteractions_ChatId_RequestedAtUtc' AND object_id = OBJECT_ID(N'dbo.AssistantInteractions'))
    CREATE INDEX IX_AssistantInteractions_ChatId_RequestedAtUtc ON dbo.AssistantInteractions(ChatId, RequestedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssistantInteractions_RelatedEntity' AND object_id = OBJECT_ID(N'dbo.AssistantInteractions'))
    CREATE INDEX IX_AssistantInteractions_RelatedEntity ON dbo.AssistantInteractions(RelatedEntityType, RelatedEntityId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssistantChats_LastMessageAtUtc' AND object_id = OBJECT_ID(N'dbo.AssistantChats'))
    CREATE INDEX IX_AssistantChats_LastMessageAtUtc ON dbo.AssistantChats(LastMessageAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AssistantMemoryItems_IsPinned_UpdatedAtUtc' AND object_id = OBJECT_ID(N'dbo.AssistantMemoryItems'))
    CREATE INDEX IX_AssistantMemoryItems_IsPinned_UpdatedAtUtc ON dbo.AssistantMemoryItems(IsPinned, UpdatedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TaskItems_Status_DueAtUtc' AND object_id = OBJECT_ID(N'dbo.TaskItems'))
    CREATE INDEX IX_TaskItems_Status_DueAtUtc ON dbo.TaskItems(Status, DueAtUtc);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TaskItems_PlannedFor' AND object_id = OBJECT_ID(N'dbo.TaskItems'))
    CREATE INDEX IX_TaskItems_PlannedFor ON dbo.TaskItems(PlannedFor);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Notes_CreatedAtUtc' AND object_id = OBJECT_ID(N'dbo.Notes'))
    CREATE INDEX IX_Notes_CreatedAtUtc ON dbo.Notes(CreatedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Reminders_Status_TriggerAtUtc' AND object_id = OBJECT_ID(N'dbo.Reminders'))
    CREATE INDEX IX_Reminders_Status_TriggerAtUtc ON dbo.Reminders(Status, TriggerAtUtc);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MileageEntries_RecordedAtUtc' AND object_id = OBJECT_ID(N'dbo.MileageEntries'))
    CREATE INDEX IX_MileageEntries_RecordedAtUtc ON dbo.MileageEntries(RecordedAtUtc DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SavedLocations_Name' AND object_id = OBJECT_ID(N'dbo.SavedLocations'))
    CREATE INDEX IX_SavedLocations_Name ON dbo.SavedLocations(Name);
GO


