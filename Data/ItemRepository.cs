using Microsoft.Data.SqlClient;

namespace DotNet10Sample.Data;

public class ItemRepository
{
    private const string TableName = "Items";
    private readonly string _connectionString;

    public ItemRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection が設定されていません。");
    }

    public async Task EnsureDatabaseAsync()
    {
        var builder = new SqlConnectionStringBuilder(_connectionString);
        var databaseName = builder.InitialCatalog;

        // Create database if missing.
        var masterBuilder = new SqlConnectionStringBuilder(_connectionString)
        {
            InitialCatalog = "master"
        };

        await using (var masterConnection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await masterConnection.OpenAsync();
            var createDbCommand = new SqlCommand(
                $"IF DB_ID(@dbName) IS NULL CREATE DATABASE [{databaseName}];",
                masterConnection);
            createDbCommand.Parameters.AddWithValue("@dbName", databaseName);
            await createDbCommand.ExecuteNonQueryAsync();
        }

        // Create table if missing and add new columns if absent.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTableCommand = new SqlCommand($"""
            IF OBJECT_ID(@tableName, 'U') IS NULL
            BEGIN
                CREATE TABLE [{TableName}](
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [Code] NVARCHAR(5) NOT NULL,
                    [Name] NVARCHAR(10) NOT NULL,
                    [CategoryCode] NVARCHAR(10) NULL,
                    [Remarks] NVARCHAR(100) NULL
                );
            END
            """, connection);

        createTableCommand.Parameters.AddWithValue("@tableName", TableName);
        await createTableCommand.ExecuteNonQueryAsync();

        // Ensure new columns exist when upgrading from older schema
        var addCodeColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'Code') IS NULL ALTER TABLE [{TableName}] ADD [Code] NVARCHAR(5) NOT NULL DEFAULT '';", connection);
        var addCategoryColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'CategoryCode') IS NULL ALTER TABLE [{TableName}] ADD [CategoryCode] NVARCHAR(10) NULL;", connection);
        var addRemarksColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'Remarks') IS NULL ALTER TABLE [{TableName}] ADD [Remarks] NVARCHAR(100) NULL;", connection);

        await addCodeColumn.ExecuteNonQueryAsync();
        await addCategoryColumn.ExecuteNonQueryAsync();
        await addRemarksColumn.ExecuteNonQueryAsync();

        // Create ItemCategories table if missing.
        var createCategoryTableCommand = new SqlCommand($"""
            IF OBJECT_ID(@categoryTableName, 'U') IS NULL
            BEGIN
                CREATE TABLE [ItemCategories](
                    [Code] NVARCHAR(10) NOT NULL PRIMARY KEY,
                    [Name] NVARCHAR(50) NOT NULL
                );
            END
            """, connection);

        createCategoryTableCommand.Parameters.AddWithValue("@categoryTableName", "ItemCategories");
        await createCategoryTableCommand.ExecuteNonQueryAsync();

        // Optionally add foreign key if not exists
        var addFk = new SqlCommand($"IF OBJECT_ID('FK_Items_ItemCategories', 'F') IS NULL AND OBJECT_ID('{TableName}', 'U') IS NOT NULL AND OBJECT_ID('ItemCategories', 'U') IS NOT NULL BEGIN ALTER TABLE [{TableName}] ADD CONSTRAINT FK_Items_ItemCategories FOREIGN KEY([CategoryCode]) REFERENCES [ItemCategories]([Code]); END", connection);
        await addFk.ExecuteNonQueryAsync();

        // Ensure unique index on Code
        var addUniqueIndex = new SqlCommand($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Items_Code' AND object_id = OBJECT_ID('{TableName}')) BEGIN CREATE UNIQUE INDEX [IX_Items_Code] ON [{TableName}]([Code]); END", connection);
        await addUniqueIndex.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertAsync(string itemCode, string itemName, string? categoryCode, string? remarks)
    {
        // Pre-check uniqueness
        if (await ExistsItemCodeAsync(itemCode))
        {
            throw new InvalidOperationException("品目コードは既に使用されています。");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var insertCommand = new SqlCommand($"""
            INSERT INTO [{TableName}] ([Code], [Name], [CategoryCode], [Remarks])
            OUTPUT INSERTED.Id
            VALUES (@code, @name, @category, @remarks);
            """, connection);

        insertCommand.Parameters.AddWithValue("@code", itemCode);
        insertCommand.Parameters.AddWithValue("@name", itemName);
        insertCommand.Parameters.AddWithValue("@category", (object?)categoryCode ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("@remarks", (object?)remarks ?? DBNull.Value);

        try
        {
            var result = await insertCommand.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            throw new InvalidOperationException("品目コードは既に使用されています。", ex);
        }
    }

    public async Task<List<Item>> SearchAsync(string? itemName, string? itemCode = null, string? categoryCode = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = $"""
            SELECT i.[Id], i.[Code], i.[Name], i.[CategoryCode], i.[Remarks], c.[Name] as CategoryName
            FROM [{TableName}] i
            LEFT JOIN [ItemCategories] c ON i.[CategoryCode] = c.[Code]
            """;

        var where = new List<string>();

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            where.Add("i.[Name] LIKE @name");
        }

        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            where.Add("i.[Code] = @code");
        }

        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            where.Add("i.[CategoryCode] = @category");
        }

        if (where.Any())
        {
            query += " WHERE " + string.Join(" AND ", where);
        }

        var command = new SqlCommand(query, connection);

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            command.Parameters.AddWithValue("@name", $"%{itemName}%");
        }

        if (!string.IsNullOrWhiteSpace(itemCode))
        {
            command.Parameters.AddWithValue("@code", itemCode);
        }

        if (!string.IsNullOrWhiteSpace(categoryCode))
        {
            command.Parameters.AddWithValue("@category", categoryCode);
        }

        var items = new List<Item>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new Item
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                CategoryCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                Remarks = reader.IsDBNull(4) ? null : reader.GetString(4),
                CategoryName = reader.IsDBNull(5) ? null : reader.GetString(5)
            });
        }

        return items;
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT i.[Id], i.[Code], i.[Name], i.[CategoryCode], i.[Remarks], c.[Name] as CategoryName
            FROM [{TableName}] i
            LEFT JOIN [ItemCategories] c ON i.[CategoryCode] = c.[Code]
            WHERE i.[Id] = @id
            """, connection);

        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Item
            {
                Id = reader.GetInt32(0),
                Code = reader.GetString(1),
                Name = reader.GetString(2),
                CategoryCode = reader.IsDBNull(3) ? null : reader.GetString(3),
                Remarks = reader.IsDBNull(4) ? null : reader.GetString(4),
                CategoryName = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
        }

        return null;
    }

    public async Task UpdateAsync(int id, string code, string name, string? categoryCode, string? remarks)
    {
        // Pre-check uniqueness (exclude this id)
        if (await ExistsItemCodeAsync(code, id))
        {
            throw new InvalidOperationException("品目コードは既に使用されています。");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            UPDATE [{TableName}]
            SET [Code] = @code, [Name] = @name, [CategoryCode] = @category, [Remarks] = @remarks
            WHERE [Id] = @id;
            """, connection);

        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@category", (object?)categoryCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@remarks", (object?)remarks ?? DBNull.Value);
        command.Parameters.AddWithValue("@id", id);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            throw new InvalidOperationException("品目コードは既に使用されています。", ex);
        }
    }

    // Item entity
    public class Item
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public string? Remarks { get; set; }
    }

    // ItemCategory master operations
    public class ItemCategory
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public async Task<List<ItemCategory>> GetItemCategoriesAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT [Code], [Name]
            FROM [ItemCategories]
            ORDER BY [Code]
            """, connection);

        var list = new List<ItemCategory>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ItemCategory
            {
                Code = reader.GetString(0),
                Name = reader.GetString(1)
            });
        }

        return list;
    }

    public async Task<ItemCategory?> GetItemCategoryByCodeAsync(string code)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT [Code], [Name]
            FROM [ItemCategories]
            WHERE [Code] = @code
            """, connection);

        command.Parameters.AddWithValue("@code", code);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ItemCategory
        {
            Code = reader.GetString(0),
            Name = reader.GetString(1)
        };
        }

        return null;
    }

    public async Task<bool> ExistsItemCodeAsync(string code, int? excludeId = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = excludeId.HasValue
            ? $"SELECT COUNT(1) FROM [{TableName}] WHERE [Code] = @code AND [Id] <> @id"
            : $"SELECT COUNT(1) FROM [{TableName}] WHERE [Code] = @code";

        var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@code", code);
        if (excludeId.HasValue)
        {
            command.Parameters.AddWithValue("@id", excludeId.Value);
        }

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    public async Task InsertItemCategoryAsync(string code, string name)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            INSERT INTO [ItemCategories] ([Code], [Name])
            VALUES (@code, @name);
            """, connection);

        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@name", name);

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateItemCategoryAsync(string code, string name)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            UPDATE [ItemCategories]
            SET [Name] = @name
            WHERE [Code] = @code;
            """, connection);

        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@code", code);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteItemCategoryAsync(string code)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            DELETE FROM [ItemCategories]
            WHERE [Code] = @code;
            """, connection);

        command.Parameters.AddWithValue("@code", code);

        await command.ExecuteNonQueryAsync();
    }
}




