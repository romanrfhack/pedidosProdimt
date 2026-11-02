BEGIN TRANSACTION;
ALTER TABLE [Customers] ADD [AutomaticConfirmationCutoff] time NULL;

ALTER TABLE [Customers] ADD [OrderingScheduleVersion] int NOT NULL DEFAULT 0;

ALTER TABLE [Customers] ADD CONSTRAINT [CK_Customers_AutomaticConfirmationCutoff] CHECK ([AutomaticConfirmationCutoff] IS NULL OR [AutomaticConfirmationCutoff] < '18:00:00');

CREATE TABLE [RouteOrderSchedules] (
    [RouteCode] nvarchar(80) NOT NULL,
    [AutomaticConfirmationCutoff] time NULL,
    [Version] int NOT NULL,
    CONSTRAINT [PK_RouteOrderSchedules] PRIMARY KEY ([RouteCode]),
    CONSTRAINT [CK_RouteOrderSchedules_Cutoff] CHECK ([AutomaticConfirmationCutoff] IS NULL OR [AutomaticConfirmationCutoff] < '18:00:00')
);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261103030000_AddOrderingSchedules', N'10.0.8');

COMMIT;
GO
