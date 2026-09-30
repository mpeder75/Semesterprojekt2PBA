using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.eShopWeb.Catalog.Contracts;

namespace Microsoft.eShopWeb.Catalog.Api.Data;

public static class CatalogRollback
{
    // Offline only. Stop ALL catalog writers before invoking; never called by HTTP requests.
    public static async Task RestoreAsync(string connectionString, CatalogSnapshot snapshot)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 120;
        command.Parameters.AddWithValue("@snapshot", JsonSerializer.Serialize(snapshot));
        command.CommandText = """
            SELECT Id, Brand INTO #Brands FROM OPENJSON(@snapshot, '$.Brands')
                WITH (Id int, Brand nvarchar(max));
            SELECT Id, Type INTO #Types FROM OPENJSON(@snapshot, '$.Types')
                WITH (Id int, Type nvarchar(max));
            SELECT * INTO #Items FROM OPENJSON(@snapshot, '$.Items') WITH (
                Id int, Name nvarchar(50), Description nvarchar(max), Price decimal(18,2),
                PictureUri nvarchar(max), CatalogBrandId int, CatalogTypeId int);

            -- Lock all three tables until verification and commit finish.
            DECLARE @count bigint;
            SELECT @count = COUNT_BIG(*) FROM Catalog WITH (TABLOCKX, HOLDLOCK);
            SELECT @count = COUNT_BIG(*) FROM CatalogBrands WITH (TABLOCKX, HOLDLOCK);
            SELECT @count = COUNT_BIG(*) FROM CatalogTypes WITH (TABLOCKX, HOLDLOCK);

            DELETE FROM Catalog WHERE Id NOT IN (SELECT Id FROM #Items);
            UPDATE b SET Brand=s.Brand FROM CatalogBrands b JOIN #Brands s ON b.Id=s.Id;
            UPDATE t SET Type=s.Type FROM CatalogTypes t JOIN #Types s ON t.Id=s.Id;
            IF COLUMNPROPERTY(OBJECT_ID('CatalogBrands'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT CatalogBrands ON;
            INSERT CatalogBrands (Id,Brand) SELECT Id,Brand FROM #Brands s WHERE NOT EXISTS (SELECT 1 FROM CatalogBrands b WHERE b.Id=s.Id);
            IF COLUMNPROPERTY(OBJECT_ID('CatalogBrands'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT CatalogBrands OFF;
            IF COLUMNPROPERTY(OBJECT_ID('CatalogTypes'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT CatalogTypes ON;
            INSERT CatalogTypes (Id,Type) SELECT Id,Type FROM #Types s WHERE NOT EXISTS (SELECT 1 FROM CatalogTypes t WHERE t.Id=s.Id);
            IF COLUMNPROPERTY(OBJECT_ID('CatalogTypes'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT CatalogTypes OFF;
            UPDATE i SET Name=s.Name, Description=s.Description, Price=s.Price, PictureUri=s.PictureUri,
                CatalogBrandId=s.CatalogBrandId, CatalogTypeId=s.CatalogTypeId FROM Catalog i JOIN #Items s ON i.Id=s.Id;
            IF COLUMNPROPERTY(OBJECT_ID('Catalog'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT Catalog ON;
            INSERT Catalog (Id,Name,Description,Price,PictureUri,CatalogBrandId,CatalogTypeId)
                SELECT Id,Name,Description,Price,PictureUri,CatalogBrandId,CatalogTypeId FROM #Items s
                WHERE NOT EXISTS (SELECT 1 FROM Catalog i WHERE i.Id=s.Id);
            IF COLUMNPROPERTY(OBJECT_ID('Catalog'), 'Id', 'IsIdentity')=1 SET IDENTITY_INSERT Catalog OFF;
            DELETE FROM CatalogBrands WHERE Id NOT IN (SELECT Id FROM #Brands);
            DELETE FROM CatalogTypes WHERE Id NOT IN (SELECT Id FROM #Types);

            -- The monolith uses HiLo allocation. Restart its hosts after rollback to discard cached IDs.
            DECLARE @next bigint = (SELECT ISNULL(MAX(Id),0)+11 FROM Catalog);
            DECLARE @current bigint = (SELECT CONVERT(bigint,current_value) FROM sys.sequences WHERE name='catalog_hilo' AND schema_id=SCHEMA_ID('dbo'));
            IF @current IS NOT NULL AND @next > @current
            BEGIN
                DECLARE @sql nvarchar(200)=N'ALTER SEQUENCE dbo.catalog_hilo RESTART WITH '+CONVERT(nvarchar(30),@next);
                EXEC sp_executesql @sql;
            END;
            SET @next = (SELECT ISNULL(MAX(Id),0)+11 FROM CatalogBrands);
            SET @current = (SELECT CONVERT(bigint,current_value) FROM sys.sequences WHERE name='catalog_brand_hilo' AND schema_id=SCHEMA_ID('dbo'));
            IF @current IS NOT NULL AND @next > @current
            BEGIN
                SET @sql=N'ALTER SEQUENCE dbo.catalog_brand_hilo RESTART WITH '+CONVERT(nvarchar(30),@next);
                EXEC sp_executesql @sql;
            END;
            SET @next = (SELECT ISNULL(MAX(Id),0)+11 FROM CatalogTypes);
            SET @current = (SELECT CONVERT(bigint,current_value) FROM sys.sequences WHERE name='catalog_type_hilo' AND schema_id=SCHEMA_ID('dbo'));
            IF @current IS NOT NULL AND @next > @current
            BEGIN
                SET @sql=N'ALTER SEQUENCE dbo.catalog_type_hilo RESTART WITH '+CONVERT(nvarchar(30),@next);
                EXEC sp_executesql @sql;
            END;
            """;
        await command.ExecuteNonQueryAsync();
        var restored = await CatalogTransfer.ReadLegacyAsync(connection, transaction);
        if (JsonSerializer.Serialize(restored) != JsonSerializer.Serialize(snapshot))
            throw new InvalidOperationException("Rollback verification failed; transaction will be rolled back.");
        await transaction.CommitAsync();
    }
}
