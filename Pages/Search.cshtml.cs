using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

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

    public List<ItemRepository.ItemCategory> Categories { get; private set; } = new();

    public List<ItemRepository.Item> SearchResults { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Categories = await _repository.GetItemCategoriesAsync();

        if (!string.IsNullOrWhiteSpace(ItemName) || !string.IsNullOrWhiteSpace(ItemCode) || !string.IsNullOrWhiteSpace(CategoryCode))
        {
            try
            {
                SearchResults = await _repository.SearchAsync(ItemName, ItemCode, CategoryCode);
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
            SearchResults = await _repository.SearchAsync(ItemName, ItemCode, CategoryCode);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "検索に失敗しました。接続設定やSQL Serverの状態を確認してください。");
        }

        return Page();
    }
}

