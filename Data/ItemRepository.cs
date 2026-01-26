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

        // Create table if missing.
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTableCommand = new SqlCommand($"""
            IF OBJECT_ID(@tableName, 'U') IS NULL
            BEGIN
                CREATE TABLE [{TableName}](
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [Name] NVARCHAR(10) NOT NULL
                );
            END
            """, connection);

        createTableCommand.Parameters.AddWithValue("@tableName", TableName);
        await createTableCommand.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertAsync(string itemName)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var insertCommand = new SqlCommand($"""
            INSERT INTO [{TableName}] ([Name])
            OUTPUT INSERTED.Id
            VALUES (@name);
            """, connection);

        insertCommand.Parameters.AddWithValue("@name", itemName);
        var result = await insertCommand.ExecuteScalarAsync();

        return Convert.ToInt32(result);
    }

    public async Task<List<Item>> SearchAsync(string? itemName)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = $"""
            SELECT [Id], [Name]
            FROM [{TableName}]
            """;

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            query += " WHERE [Name] LIKE @name";
        }

        var command = new SqlCommand(query, connection);

        if (!string.IsNullOrWhiteSpace(itemName))
        {
            command.Parameters.AddWithValue("@name", $"%{itemName}%");
        }

        var items = new List<Item>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new Item
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT [Id], [Name]
            FROM [{TableName}]
            WHERE [Id] = @id
            """, connection);

        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Item
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            };
        }

        return null;
    }

    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}




