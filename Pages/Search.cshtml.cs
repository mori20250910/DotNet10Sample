using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Linq;

namespace DotNet10Sample.Pages;

public class SearchModel : PageModel
{
    private readonly ItemRepository _repository;

    public SearchModel(ItemRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? ItemName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ItemCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CategoryCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsReturningFromDetail { get; set; }

    public List<ItemRepository.ItemCategory> Categories { get; private set; } = new();

    public List<ItemRepository.Item> SearchResults { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _repository.GetItemCategoriesAsync();

        // Search if user has entered search conditions OR if returning from detail view
        bool hasSearchConditions = !string.IsNullOrWhiteSpace(ItemName) || !string.IsNullOrWhiteSpace(ItemCode) || !string.IsNullOrWhiteSpace(CategoryCode);
        
        if (hasSearchConditions || IsReturningFromDetail)
        {
            try
            {
                bool? categoryIsNull = CategoryCode == "__NULL__" ? true : null;
                var categoryParam = CategoryCode == "__NULL__" ? null : CategoryCode;
                SearchResults = await _repository.SearchAsync(ItemName, ItemCode, categoryParam, categoryIsNull);
            }
            catch (Exception)
            {
                ModelState.AddModelError(string.Empty, "検索に失敗しました。接続設定やSQL Serverの状態を確認してください。");
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Categories = await _repository.GetItemCategoriesAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            bool? categoryIsNull = CategoryCode == "__NULL__" ? true : null;
            var categoryParam = CategoryCode == "__NULL__" ? null : CategoryCode;
            SearchResults = await _repository.SearchAsync(ItemName, ItemCode, categoryParam, categoryIsNull);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "検索に失敗しました。接続設定やSQL Serverの状態を確認してください。");
        }

        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync()
    {
        try
        {
            bool? categoryIsNull = CategoryCode == "__NULL__" ? true : null;
            var categoryParam = CategoryCode == "__NULL__" ? null : CategoryCode;
            var list = await _repository.SearchAsync(ItemName, ItemCode, categoryParam, categoryIsNull);

            var sb = new StringBuilder();
            sb.AppendLine("ID,品目コード,品目名,カテゴリ,製造開始年月日,備考");

            foreach (var item in list)
            {
                static string Escape(string? s)
                {
                    if (string.IsNullOrEmpty(s)) return "";
                    return '"' + s.Replace("\"", "\"\"") + '"';
                }

                var dateStr = item.ManufactureStartDate.HasValue ? item.ManufactureStartDate.Value.ToString("yyyy-MM-dd") : string.Empty;
                sb.AppendLine($"{item.Id},{Escape(item.Code)},{Escape(item.Name)},{Escape(item.CategoryName)},{Escape(dateStr)},{Escape(item.Remarks)}");
            }

            // Use UTF-8 with BOM so Excel on Windows opens it correctly
            var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            var fileName = $"search_results_{DateTime.Now:yyyyMMddHHmmss}.csv";
            return File(content, "text/csv; charset=utf-8", fileName);
        }
        catch (Exception)
        {
            return StatusCode(500, "CSVの生成に失敗しました。");
        }
    }
}

