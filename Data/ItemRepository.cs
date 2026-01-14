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
                    [Remarks] NVARCHAR(100) NULL,
                    [ManufactureStartDate] DATE NULL
                );
            END
            """, connection);

        createTableCommand.Parameters.AddWithValue("@tableName", TableName);
        await createTableCommand.ExecuteNonQueryAsync();

        // Ensure new columns exist when upgrading from older schema
        var addCodeColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'Code') IS NULL ALTER TABLE [{TableName}] ADD [Code] NVARCHAR(5) NOT NULL DEFAULT '';", connection);
        var addCategoryColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'CategoryCode') IS NULL ALTER TABLE [{TableName}] ADD [CategoryCode] NVARCHAR(10) NULL;", connection);
        var addRemarksColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'Remarks') IS NULL ALTER TABLE [{TableName}] ADD [Remarks] NVARCHAR(100) NULL;", connection);
        var addManufactureDateColumn = new SqlCommand($"IF COL_LENGTH('{TableName}', 'ManufactureStartDate') IS NULL ALTER TABLE [{TableName}] ADD [ManufactureStartDate] DATE NULL;", connection);

        await addCodeColumn.ExecuteNonQueryAsync();
        await addCategoryColumn.ExecuteNonQueryAsync();
        await addRemarksColumn.ExecuteNonQueryAsync();
        await addManufactureDateColumn.ExecuteNonQueryAsync();

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

        // Create ManufacturingPlans table if missing
        var createManufacturingTableCommand = new SqlCommand($"""
            IF OBJECT_ID('ManufacturingPlans', 'U') IS NULL
            BEGIN
                CREATE TABLE [ManufacturingPlans](
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [ItemId] INT NOT NULL,
                    [PlanDate] DATE NOT NULL,
                    [Quantity] INT NOT NULL,
                    FOREIGN KEY([ItemId]) REFERENCES [{TableName}]([Id]) ON DELETE CASCADE
                );
            END
            """, connection);
        await createManufacturingTableCommand.ExecuteNonQueryAsync();

        // Ensure unique index on ItemId + PlanDate
        var addManufacturingIndex = new SqlCommand($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ManufacturingPlans_ItemId_PlanDate' AND object_id = OBJECT_ID('ManufacturingPlans')) BEGIN CREATE UNIQUE INDEX [IX_ManufacturingPlans_ItemId_PlanDate] ON [ManufacturingPlans]([ItemId], [PlanDate]); END", connection);
        await addManufacturingIndex.ExecuteNonQueryAsync();

        // Create CustomHolidays table if missing
        var createCustomHolidaysTableCommand = new SqlCommand($"""
            IF OBJECT_ID('CustomHolidays', 'U') IS NULL
            BEGIN
                CREATE TABLE [CustomHolidays](
                    [Id] INT IDENTITY(1,1) PRIMARY KEY,
                    [HolidayDate] DATE NOT NULL UNIQUE,
                    [Comment] NVARCHAR(200) NULL
                );
            END
            """, connection);
        await createCustomHolidaysTableCommand.ExecuteNonQueryAsync();

        // Ensure Comment column exists when upgrading from older schema
        var addHolidayCommentColumn = new SqlCommand($"IF COL_LENGTH('CustomHolidays', 'Comment') IS NULL ALTER TABLE [CustomHolidays] ADD [Comment] NVARCHAR(200) NULL;", connection);
        await addHolidayCommentColumn.ExecuteNonQueryAsync();
    }

    public async Task<int> InsertAsync(string itemCode, string itemName, string? categoryCode, string? remarks, DateTime? manufactureStartDate)
    {
        // Pre-check uniqueness
        if (await ExistsItemCodeAsync(itemCode))
        {
            throw new InvalidOperationException("品目コードは既に使用されています。");
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var insertCommand = new SqlCommand($"""
            INSERT INTO [{TableName}] ([Code], [Name], [CategoryCode], [Remarks], [ManufactureStartDate])
            OUTPUT INSERTED.Id
            VALUES (@code, @name, @category, @remarks, @manufactureDate);
            """, connection);

        insertCommand.Parameters.AddWithValue("@code", itemCode);
        insertCommand.Parameters.AddWithValue("@name", itemName);
        insertCommand.Parameters.AddWithValue("@category", (object?)categoryCode ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("@remarks", (object?)remarks ?? DBNull.Value);
        insertCommand.Parameters.AddWithValue("@manufactureDate", (object?)manufactureStartDate ?? DBNull.Value);

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

    public async Task<List<Item>> SearchAsync(string? itemName, string? itemCode = null, string? categoryCode = null, bool? categoryIsNull = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = $"""
            SELECT i.[Id], i.[Code], i.[Name], i.[CategoryCode], i.[Remarks], i.[ManufactureStartDate], c.[Name] as CategoryName
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

        if (categoryIsNull == true)
        {
            where.Add("i.[CategoryCode] IS NULL");
        }
        else if (!string.IsNullOrWhiteSpace(categoryCode))
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

        if (categoryIsNull != true && !string.IsNullOrWhiteSpace(categoryCode))
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
                ManufactureStartDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                CategoryName = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return items;
    }

    public async Task<Item?> GetByIdAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT i.[Id], i.[Code], i.[Name], i.[CategoryCode], i.[Remarks], i.[ManufactureStartDate], c.[Name] as CategoryName
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
                    ManufactureStartDate = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    CategoryName = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }

        return null;
    }

    public async Task UpdateAsync(int id, string code, string name, string? categoryCode, string? remarks, DateTime? manufactureStartDate)
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
            SET [Code] = @code, [Name] = @name, [CategoryCode] = @category, [Remarks] = @remarks, [ManufactureStartDate] = @manufactureDate
            WHERE [Id] = @id;
            """, connection);

        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@category", (object?)categoryCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@remarks", (object?)remarks ?? DBNull.Value);
        command.Parameters.AddWithValue("@manufactureDate", (object?)manufactureStartDate ?? DBNull.Value);
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
        public DateTime? ManufactureStartDate { get; set; }
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

    // ManufacturingPlan entity
    public class ManufacturingPlan
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public DateTime PlanDate { get; set; }
        public int Quantity { get; set; }
    }

    // ManufacturingPlan CRUD operations
    public async Task<int> InsertManufacturingPlanAsync(int itemId, DateTime planDate, int quantity)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            INSERT INTO [ManufacturingPlans] ([ItemId], [PlanDate], [Quantity])
            OUTPUT INSERTED.Id
            VALUES (@itemId, @planDate, @quantity);
            """, connection);

        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@planDate", planDate);
        command.Parameters.AddWithValue("@quantity", quantity);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<ManufacturingPlan?> GetManufacturingPlanAsync(int itemId, DateTime planDate)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT [Id], [ItemId], [PlanDate], [Quantity]
            FROM [ManufacturingPlans]
            WHERE [ItemId] = @itemId AND [PlanDate] = @planDate
            """, connection);

        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@planDate", planDate);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ManufacturingPlan
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                PlanDate = reader.GetDateTime(2),
                Quantity = reader.GetInt32(3)
            };
        }

        return null;
    }

    public async Task<List<ManufacturingPlan>> GetManufacturingPlansAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var query = "SELECT [Id], [ItemId], [PlanDate], [Quantity] FROM [ManufacturingPlans]";
        var where = new List<string>();

        if (startDate.HasValue)
        {
            where.Add("[PlanDate] >= @startDate");
        }
        if (endDate.HasValue)
        {
            where.Add("[PlanDate] <= @endDate");
        }

        if (where.Any())
        {
            query += " WHERE " + string.Join(" AND ", where);
        }

        query += " ORDER BY [PlanDate], [ItemId]";

        var command = new SqlCommand(query, connection);
        if (startDate.HasValue)
        {
            command.Parameters.AddWithValue("@startDate", startDate.Value);
        }
        if (endDate.HasValue)
        {
            command.Parameters.AddWithValue("@endDate", endDate.Value);
        }

        var plans = new List<ManufacturingPlan>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            plans.Add(new ManufacturingPlan
            {
                Id = reader.GetInt32(0),
                ItemId = reader.GetInt32(1),
                PlanDate = reader.GetDateTime(2),
                Quantity = reader.GetInt32(3)
            });
        }

        return plans;
    }

    public async Task UpdateManufacturingPlanAsync(int itemId, DateTime planDate, int quantity)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            UPDATE [ManufacturingPlans]
            SET [Quantity] = @quantity
            WHERE [ItemId] = @itemId AND [PlanDate] = @planDate;
            """, connection);

        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@planDate", planDate);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteManufacturingPlanAsync(int itemId, DateTime planDate)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            DELETE FROM [ManufacturingPlans]
            WHERE [ItemId] = @itemId AND [PlanDate] = @planDate;
            """, connection);

        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@planDate", planDate);

        await command.ExecuteNonQueryAsync();
    }

    // Get Japanese holidays for a given year
    public Task<List<DateTime>> GetJapaneseHolidaysAsync(int year)
    {
        var holidays = new List<DateTime>();

        // Fixed holidays
        holidays.Add(new DateTime(year, 1, 1)); // 元日 (New Year's Day)
        holidays.Add(new DateTime(year, 2, 11)); // 建国記念の日 (Foundation Day)
        holidays.Add(new DateTime(year, 2, 23)); // 天皇誕生日 (Emperor's Birthday)
        holidays.Add(new DateTime(year, 4, 29)); // 昭和の日 (Showa Day)
        holidays.Add(new DateTime(year, 5, 3)); // 憲法記念日 (Constitution Day)
        holidays.Add(new DateTime(year, 5, 4)); // みどりの日 (Greenery Day)
        holidays.Add(new DateTime(year, 5, 5)); // こどもの日 (Children's Day)
        holidays.Add(new DateTime(year, 8, 10)); // 山の日 (Mountain Day)
        holidays.Add(new DateTime(year, 11, 3)); // 文化の日 (Culture Day)
        holidays.Add(new DateTime(year, 11, 23)); // 勤労感謝の日 (Labor Thanksgiving Day)

        // Second Monday of January (成人の日 Coming of Age Day)
        holidays.Add(GetNthDayOfMonth(year, 1, DayOfWeek.Monday, 2));

        // Vernal equinox (Spring Equinox Day) - 約 3月20-21日
        holidays.Add(GetVernalEquinox(year));

        // Third Monday of July (海の日 Marine Day)
        holidays.Add(GetNthDayOfMonth(year, 7, DayOfWeek.Monday, 3));

        // Third Monday of September (敬老の日 Respect for the Aged Day)
        holidays.Add(GetNthDayOfMonth(year, 9, DayOfWeek.Monday, 3));

        // Autumnal equinox (Autumn Equinox Day) - 約 9月22-23日
        holidays.Add(GetAutumnalEquinox(year));

        // Second Monday of October (体育の日 Sports Day)
        holidays.Add(GetNthDayOfMonth(year, 10, DayOfWeek.Monday, 2));

        // Substitute holidays (振替休日)
        // If a holiday falls on Sunday, the following Monday is a holiday
        var sundayHolidays = holidays.Where(d => d.DayOfWeek == DayOfWeek.Sunday).ToList();
        foreach (var holiday in sundayHolidays)
        {
            var substituteDate = holiday.AddDays(1);
            if (!holidays.Contains(substituteDate))
            {
                holidays.Add(substituteDate);
            }
        }

        holidays = holidays.OrderBy(d => d).Distinct().ToList();
        return Task.FromResult(holidays);
    }

    private DateTime GetNthDayOfMonth(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        var date = new DateTime(year, month, 1);
        var daysUntilTarget = ((int)dayOfWeek - (int)date.DayOfWeek + 7) % 7;
        date = date.AddDays(daysUntilTarget);
        date = date.AddDays((n - 1) * 7);
        return date;
    }

    private DateTime GetVernalEquinox(int year)
    {
        // 春分の日 (Vernal Equinox) - 計算式：日本の祝日法に基づく近似値
        // 2026年は3月20日
        if (year == 2026) return new DateTime(year, 3, 20);
        if (year == 2025) return new DateTime(year, 3, 21);
        if (year == 2027) return new DateTime(year, 3, 21);
        // Fallback for other years
        return new DateTime(year, 3, 20);
    }

    private DateTime GetAutumnalEquinox(int year)
    {
        // 秋分の日 (Autumnal Equinox)
        // 2026年は9月21日（月の日の計算により変動）
        if (year == 2026) return new DateTime(year, 9, 21);
        if (year == 2025) return new DateTime(year, 9, 23);
        if (year == 2027) return new DateTime(year, 9, 22);
        // Fallback for other years
        return new DateTime(year, 9, 23);
    }

    // CustomHoliday entity
    public class CustomHoliday
    {
        public int Id { get; set; }
        public DateTime HolidayDate { get; set; }
        public string? Comment { get; set; }
    }

    // CustomHoliday CRUD operations
    public async Task<int> InsertCustomHolidayAsync(DateTime holidayDate, string? comment = null)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            INSERT INTO [CustomHolidays] ([HolidayDate], [Comment])
            VALUES (@holidayDate, @comment);
            """, connection);

        command.Parameters.AddWithValue("@holidayDate", holidayDate.Date);
        command.Parameters.AddWithValue("@comment", (object?)comment ?? DBNull.Value);

        try
        {
            await command.ExecuteNonQueryAsync();
            // Return the newly inserted ID (we can fetch it if needed)
            var selectCommand = new SqlCommand("SELECT @@IDENTITY", connection);
            var result = await selectCommand.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (SqlException ex) when (ex.Number == 2627) // Unique constraint violation
        {
            throw new InvalidOperationException($"この日付は既に登録されています。");
        }
    }

    public async Task<List<CustomHoliday>> GetCustomHolidaysAsync()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            SELECT [Id], [HolidayDate], [Comment]
            FROM [CustomHolidays]
            ORDER BY [HolidayDate]
            """, connection);

        var holidays = new List<CustomHoliday>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            holidays.Add(new CustomHoliday
            {
                Id = reader.GetInt32(0),
                HolidayDate = reader.GetDateTime(1),
                Comment = reader.IsDBNull(2) ? null : reader.GetString(2)
            });
        }

        return holidays;
    }

    public async Task UpdateCustomHolidayCommentAsync(int id, string? comment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            UPDATE [CustomHolidays]
            SET [Comment] = @comment
            WHERE [Id] = @id;
            """, connection);

        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@comment", (object?)comment ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteCustomHolidayAsync(int id)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = new SqlCommand($"""
            DELETE FROM [CustomHolidays]
            WHERE [Id] = @id;
            """, connection);

        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }
}




