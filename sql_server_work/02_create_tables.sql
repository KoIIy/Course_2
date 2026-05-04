USE AnketaOK;
GO

IF OBJECT_ID(N'dbo.FormTemplates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FormTemplates
    (
        TemplateId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FormTemplates PRIMARY KEY,
        TemplateName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_FormTemplates_IsActive DEFAULT 1
    );
END
GO

IF OBJECT_ID(N'dbo.TemplatePages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.TemplatePages
    (
        TemplatePageId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TemplatePages PRIMARY KEY,
        TemplateId INT NOT NULL,
        PageNumber INT NOT NULL,
        ImageData VARBINARY(MAX) NOT NULL,
        ImageWidth INT NOT NULL,
        ImageHeight INT NOT NULL,

        CONSTRAINT FK_TemplatePages_FormTemplates
            FOREIGN KEY (TemplateId) REFERENCES dbo.FormTemplates(TemplateId)
            ON DELETE CASCADE,
        CONSTRAINT UQ_TemplatePages_Template_Page UNIQUE (TemplateId, PageNumber),
        CONSTRAINT CK_TemplatePages_PageNumber CHECK (PageNumber > 0),
        CONSTRAINT CK_TemplatePages_ImageSize CHECK (ImageWidth > 0 AND ImageHeight > 0)
    );
END
GO

IF OBJECT_ID(N'dbo.RequiredFieldAreas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RequiredFieldAreas
    (
        RequiredFieldAreaId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RequiredFieldAreas PRIMARY KEY,
        TemplatePageId INT NOT NULL,
        X INT NOT NULL,
        Y INT NOT NULL,
        Width INT NOT NULL,
        Height INT NOT NULL,

        CONSTRAINT FK_RequiredFieldAreas_TemplatePages
            FOREIGN KEY (TemplatePageId) REFERENCES dbo.TemplatePages(TemplatePageId)
            ON DELETE CASCADE,
        CONSTRAINT CK_RequiredFieldAreas_Coordinates CHECK
        (
            X >= 0 AND Y >= 0 AND Width > 0 AND Height > 0
        )
    );
END
GO

IF OBJECT_ID(N'dbo.CheckboxAreas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CheckboxAreas
    (
        CheckboxAreaId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CheckboxAreas PRIMARY KEY,
        TemplatePageId INT NOT NULL,
        GroupNumber INT NOT NULL,
        X INT NOT NULL,
        Y INT NOT NULL,
        Width INT NOT NULL,
        Height INT NOT NULL,

        CONSTRAINT FK_CheckboxAreas_TemplatePages
            FOREIGN KEY (TemplatePageId) REFERENCES dbo.TemplatePages(TemplatePageId)
            ON DELETE CASCADE,
        CONSTRAINT CK_CheckboxAreas_GroupNumber CHECK (GroupNumber > 0),
        CONSTRAINT CK_CheckboxAreas_Coordinates CHECK
        (
            X >= 0 AND Y >= 0 AND Width > 0 AND Height > 0
        )
    );
END
GO

IF OBJECT_ID(N'dbo.CompletedForms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompletedForms
    (
        CompletedFormId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompletedForms PRIMARY KEY,
        TemplateId INT NOT NULL,
        UploadedBy NVARCHAR(200) NULL,
        UploadedAt DATETIME2(0) NOT NULL CONSTRAINT DF_CompletedForms_UploadedAt DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_CompletedForms_FormTemplates
            FOREIGN KEY (TemplateId) REFERENCES dbo.FormTemplates(TemplateId)
    );
END
GO

IF OBJECT_ID(N'dbo.CompletedFormPages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompletedFormPages
    (
        CompletedFormPageId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CompletedFormPages PRIMARY KEY,
        CompletedFormId INT NOT NULL,
        TemplatePageId INT NOT NULL,
        PageNumber INT NOT NULL,
        ImageData VARBINARY(MAX) NOT NULL,

        CONSTRAINT FK_CompletedFormPages_CompletedForms
            FOREIGN KEY (CompletedFormId) REFERENCES dbo.CompletedForms(CompletedFormId)
            ON DELETE CASCADE,
        CONSTRAINT FK_CompletedFormPages_TemplatePages
            FOREIGN KEY (TemplatePageId) REFERENCES dbo.TemplatePages(TemplatePageId),
        CONSTRAINT UQ_CompletedFormPages_Form_Page UNIQUE (CompletedFormId, PageNumber),
        CONSTRAINT CK_CompletedFormPages_PageNumber CHECK (PageNumber > 0)
    );
END
GO
