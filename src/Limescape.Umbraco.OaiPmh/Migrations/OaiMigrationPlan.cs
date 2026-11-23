using Limescape.Umbraco.OaiPmh.Records;
using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Limescape.Umbraco.OaiPmh.Migrations;

public sealed class OaiMigrationPlan() : PackageMigrationPlan("Limescape.Umbraco.OaiPmh", "Limescape.Umbraco.OaiPmh", "Limescape.Umbraco.OaiPmh")
{
    protected override void DefinePlan() => To<CreateRecordTable>("ls-oai-001-records");
}

public sealed class CreateRecordTable(IMigrationContext context) : AsyncMigrationBase(context)
{
    protected override Task MigrateAsync()
    {
        if (!SqlSyntax.DoesTableExist(Database, "lsOaiRecord"))
        {
            Create.Table<RecordDto>().Do();
        }

        return Task.CompletedTask;
    }
}
