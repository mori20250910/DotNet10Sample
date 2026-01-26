using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DotNet10Sample.Pages;

public class DetailModel : PageModel
{
    private readonly ItemRepository _repository;

    public DetailModel(ItemRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public ItemRepository.Item? Item { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id <= 0)
        {
            return RedirectToPage("/Search");
        }

        try
        {
            Item = await _repository.GetByIdAsync(Id);
            if (Item == null)
            {
                TempData["ErrorMessage"] = "指定された品目が見つかりませんでした。";
                return RedirectToPage("/Search");
            }
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "詳細情報の取得に失敗しました。接続設定やSQL Serverの状態を確認してください。";
            return RedirectToPage("/Search");
        }

        return Page();
    }
}


