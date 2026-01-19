using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;


namespace DotNet10Sample.Pages;

public class ManufacturingModel : PageModel
{
    private readonly ItemRepository _repository;

    public ManufacturingModel(ItemRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? YearMonth { get; set; }

    public DateTime? StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }

    public List<ItemRepository.Item> Items { get; private set; } = new();
    public List<DateTime> PlanDates { get; private set; } = new();
    public Dictionary<(int itemId, DateTime date), int> PlanData { get; private set; } = new();
    public Dictionary<int, DateTime?> ItemManufactureStartDates { get; private set; } = new();
    public HashSet<DateTime> Holidays { get; private set; } = new();

    // Japanese national holidays (dates only)
    public HashSet<DateTime> JapaneseHolidays { get; private set; } = new();

    // Map of date -> holiday name or custom comment (shown in popup)
    public Dictionary<DateTime, string?> HolidayLabels { get; private set; } = new();

    public async Task OnGetAsync()
    {
        // Default to current month if not specified
        var now = DateTime.Now;
        YearMonth ??= now.ToString("yyyy-MM");

        // Parse year and month from YearMonth string (format: yyyy-MM)
        if (DateTime.TryParse(YearMonth + "-01", out var monthDate))
        {
            StartDate = new DateTime(monthDate.Year, monthDate.Month, 1);
            EndDate = StartDate.Value.AddMonths(1).AddDays(-1);
        }
        else
        {
            // Fallback to current month
            StartDate = new DateTime(now.Year, now.Month, 1);
            EndDate = StartDate.Value.AddMonths(1).AddDays(-1);
        }

        // Get all items
        Items = await _repository.SearchAsync(null);
        
        // Build item manufacture start date lookup
        foreach (var item in Items)
        {
            ItemManufactureStartDates[item.Id] = item.ManufactureStartDate;
        }

        // Generate date range for display (all dates in the range)
        PlanDates = new List<DateTime>();
        var currentDate = StartDate.Value;
        while (currentDate <= EndDate.Value)
        {
            PlanDates.Add(currentDate);
            currentDate = currentDate.AddDays(1);
        }

        // Get holidays for the year range
        var holidaysList = new HashSet<DateTime>();
        for (var year = StartDate.Value.Year; year <= EndDate.Value.Year; year++)
        {
            var yearHolidays = await _repository.GetJapaneseHolidaysAsync(year);
            foreach (var holiday in yearHolidays)
            {
                holidaysList.Add(holiday.Date);
            }
        }

        // Add custom holidays from database
        var customHolidays = await _repository.GetCustomHolidaysAsync();
        foreach (var customHoliday in customHolidays)
        {
            holidaysList.Add(customHoliday.HolidayDate.Date);
        }

        // Add weekends (Saturday and Sunday) as holidays
        foreach (var date in PlanDates)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                holidaysList.Add(date.Date);
            }
        }

        Holidays = holidaysList;

        // Build holiday labels (holiday name or custom comment)
        var holidayLabels = new Dictionary<DateTime, string?>();

        // Japanese holidays with names
        var japaneseHolidayDates = new HashSet<DateTime>();
        for (var year = StartDate.Value.Year; year <= EndDate.Value.Year; year++)
        {
            var namedHolidays = await _repository.GetJapaneseHolidaysWithNamesAsync(year);
            foreach (var nh in namedHolidays)
            {
                var d = nh.Date.Date;
                if (!holidayLabels.ContainsKey(d))
                {
                    holidayLabels[d] = nh.Name;
                }
                japaneseHolidayDates.Add(d);
            }
        }
        JapaneseHolidays = japaneseHolidayDates;

        // Custom holidays (comments) - override name if comment present
        foreach (var ch in customHolidays)
        {
            holidayLabels[ch.HolidayDate.Date] = string.IsNullOrWhiteSpace(ch.Comment) ? "休日" : ch.Comment;
        }

        // Weekends - if no label set, mark as '休日'
        foreach (var date in PlanDates)
        {
            if ((date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) && !holidayLabels.ContainsKey(date.Date))
            {
                holidayLabels[date.Date] = "休日";
            }
        }

        HolidayLabels = holidayLabels;

        // Load existing plans
        var plans = await _repository.GetManufacturingPlansAsync(StartDate, EndDate);
        foreach (var plan in plans)
        {
            PlanData[(plan.ItemId, plan.PlanDate)] = plan.Quantity;
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        // Parse YearMonth from request to get start and end dates
        var yearMonthStr = Request.Form["YearMonth"].ToString();
        if (string.IsNullOrWhiteSpace(yearMonthStr) || !DateTime.TryParse(yearMonthStr + "-01", out var monthDate))
        {
            return BadRequest(new { success = false, errors = new[] { "月が指定されていません。" } });
        }

        var startDate = new DateTime(monthDate.Year, monthDate.Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        // Get all items
        Items = await _repository.SearchAsync(null);

        var errors = new List<string>();

        // Process form data
        foreach (var key in Request.Form.Keys)
        {
            if (!key.StartsWith("qty_"))
                continue;

            // Parse key format: qty_itemId_date (date format: yyyy-MM-dd)
            var parts = key.Split('_');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var itemId) || !DateTime.TryParse(parts[2], out var planDate))
                continue;

            var quantityStr = Request.Form[key].ToString().Trim();

            // Find the item to check manufacture start date
            var item = Items.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
                continue;

            // Check if quantity is empty
            if (string.IsNullOrWhiteSpace(quantityStr))
            {
                // Empty is allowed, delete if exists
                await _repository.DeleteManufacturingPlanAsync(itemId, planDate);
                continue;
            }

            // Validate quantity
            if (!int.TryParse(quantityStr, out var quantity) || quantity < 1 || quantity > 99)
            {
                errors.Add($"品目ID {itemId} の {planDate:MM/dd} の製造数は1～99の数字のみ入力可能です。");
                continue;
            }

            // Check manufacture start date
            if (item.ManufactureStartDate.HasValue && planDate < item.ManufactureStartDate.Value)
            {
                errors.Add($"品目「{item.Name}」の製造開始年月日は {item.ManufactureStartDate.Value:yyyy年M月d日} です。それより前の日付には入力できません。");
                continue;
            }

            // Save or update the plan
            var existingPlan = await _repository.GetManufacturingPlanAsync(itemId, planDate);
            if (existingPlan != null)
            {
                await _repository.UpdateManufacturingPlanAsync(itemId, planDate, quantity);
            }
            else
            {
                await _repository.InsertManufacturingPlanAsync(itemId, planDate, quantity);
            }
        }

        if (errors.Any())
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, errors = errors.ToArray() });
            }
            ModelState.AddModelError(string.Empty, string.Join(" ", errors));
        }

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return new JsonResult(new { success = errors.Count == 0, message = errors.Count == 0 ? "保存しました。" : null, errors = errors.ToArray() });
        }

        TempData["SuccessMessage"] = "製造計画を保存しました。";
        return RedirectToPage("/Manufacturing", new { YearMonth = yearMonthStr });
    }

    public async Task<IActionResult> OnPostExportPdfAsync()
    {
        // Parse YearMonth from request to get start and end dates
        var yearMonthStr = Request.Form["YearMonth"].ToString();
        if (string.IsNullOrWhiteSpace(yearMonthStr) || !DateTime.TryParse(yearMonthStr + "-01", out var monthDate))
        {
            return BadRequest(new { success = false, message = "月が指定されていません。" });
        }

        var startDate = new DateTime(monthDate.Year, monthDate.Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        try
        {
            // Get all items
            Items = await _repository.SearchAsync(null);

            // Generate date range for display
            PlanDates = new List<DateTime>();
            var currentDate = startDate;
            while (currentDate <= endDate)
            {
                PlanDates.Add(currentDate);
                currentDate = currentDate.AddDays(1);
            }

            // Load existing plans
            var plans = await _repository.GetManufacturingPlansAsync(startDate, endDate);
            PlanData = new Dictionary<(int itemId, DateTime date), int>();
            foreach (var plan in plans)
            {
                PlanData[(plan.ItemId, plan.PlanDate)] = plan.Quantity;
            }

            // Create PDF document using iText7
            var memoryStream = new MemoryStream();
            var writer = new PdfWriter(memoryStream);
            var pdf = new PdfDocument(writer);
            var document = new Document(pdf);

            // Set narrow margins
            document.SetMargins(20, 20, 20, 20);

            // Title
            var titleText = $"製造計画 ({startDate:yyyy年M月d日} ～ {endDate:M月d日})";
            document.Add(new Paragraph(titleText).SetFontSize(16).SetMarginBottom(15));

            // Create table with item names and dates
            int numColumns = 1 + PlanDates.Count;
            var table = new Table(numColumns);
            table.SetWidth(iText.Layout.Properties.UnitValue.CreatePercentValue(100));

            // Header row - Item column
            var itemHeaderCell = new Cell(1, 1)
                .Add(new Paragraph("品目").SetFontSize(10))
                .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(200, 200, 200));
            table.AddHeaderCell(itemHeaderCell);

            // Header row - Date columns
            foreach (var date in PlanDates)
            {
                var dateStr = $"{date:M/d}";
                var dateCell = new Cell(1, 1)
                    .Add(new Paragraph(dateStr).SetFontSize(9).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER))
                    .SetBackgroundColor(new iText.Kernel.Colors.DeviceRgb(200, 200, 200));
                table.AddHeaderCell(dateCell);
            }

            // Data rows
            foreach (var item in Items)
            {
                // Item name cell
                var nameCell = new Cell(1, 1)
                    .Add(new Paragraph(item.Name).SetFontSize(9));
                table.AddCell(nameCell);

                // Quantity cells
                foreach (var date in PlanDates)
                {
                    var quantity = PlanData.ContainsKey((item.Id, date)) ? PlanData[(item.Id, date)] : 0;
                    var qtyStr = quantity > 0 ? quantity.ToString() : "";
                    
                    var qtyCell = new Cell(1, 1)
                        .Add(new Paragraph(qtyStr).SetFontSize(9).SetTextAlignment(iText.Layout.Properties.TextAlignment.CENTER));
                    table.AddCell(qtyCell);
                }
            }

            document.Add(table);

            // Footer
            document.Add(new Paragraph($"生成日時: {DateTime.Now:yyyy年MM月dd日 HH:mm:ss}")
                .SetFontSize(8)
                .SetMarginTop(15));

            document.Close();

            // Get PDF bytes after document is closed but before stream is disposed
            byte[] pdfBytes = memoryStream.ToArray();
            memoryStream.Dispose();
            
            return File(pdfBytes, "application/pdf", $"manufacturing_{yearMonthStr}.pdf");
        }
        catch (Exception ex)
        {
            // Log the exception details
            Console.WriteLine($"PDF generation error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            
            // Get more detailed error info
            var errorDetails = new { ex.Message, StackTrace = ex.StackTrace };
            return StatusCode(500, new { success = false, message = $"PDF生成に失敗しました。エラー: {ex.Message}", details = errorDetails });
        }
    }
}
