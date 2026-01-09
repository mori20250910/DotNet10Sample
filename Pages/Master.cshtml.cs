using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DotNet10Sample.Pages;

public class MasterModel : PageModel
{
    private readonly ItemRepository _repository;

    public MasterModel(ItemRepository repository)
    {
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? SelectedMaster { get; set; }

    public List<ItemRepository.ItemCategory> Categories { get; private set; } = new();

    [BindProperty]
    public string NewCategoryCode { get; set; } = string.Empty;
    [BindProperty]
    public string NewCategoryName { get; set; } = string.Empty;

    [BindProperty]
    public string EditCode { get; set; } = string.Empty;

    [BindProperty]
    public string EditName { get; set; } = string.Empty;

    [BindProperty]
    public string DeleteCode { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        if (SelectedMaster == "ItemCategory")
        {
            Categories = await _repository.GetItemCategoriesAsync();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryCode) || string.IsNullOrWhiteSpace(NewCategoryName))
        {
            TempData["ErrorMessage"] = "コード・名前は必須です。";
            return RedirectToPage(new { SelectedMaster = "ItemCategory" });
        }

        if (NewCategoryCode.Length > 10 || NewCategoryName.Length > 50)
        {
            TempData["ErrorMessage"] = "長さの制限を超えています。（コード:10文字, 名前:50文字）";
            return RedirectToPage(new { SelectedMaster = "ItemCategory" });
        }

        try
        {
            await _repository.InsertItemCategoryAsync(NewCategoryCode, NewCategoryName);
            TempData["SuccessMessage"] = "カテゴリを追加しました。";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "カテゴリの追加に失敗しました。（重複など）";
        }

        return RedirectToPage(new { SelectedMaster = "ItemCategory" });
    }

    public async Task<IActionResult> OnPostEditCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(EditCode) || string.IsNullOrWhiteSpace(EditName))
        {
            TempData["ErrorMessage"] = "コード・名前は必須です。";
            return RedirectToPage(new { SelectedMaster = "ItemCategory" });
        }

        if (EditName.Length > 50)
        {
            TempData["ErrorMessage"] = "名前は50文字以内で入力してください。";
            return RedirectToPage(new { SelectedMaster = "ItemCategory" });
        }

        try
        {
            await _repository.UpdateItemCategoryAsync(EditCode, EditName);
            TempData["SuccessMessage"] = "カテゴリを更新しました。";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "カテゴリの更新に失敗しました。";
        }

        return RedirectToPage(new { SelectedMaster = "ItemCategory" });
    }

    public async Task<IActionResult> OnPostDeleteCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(DeleteCode))
        {
            TempData["ErrorMessage"] = "コードが指定されていません。";
            return RedirectToPage(new { SelectedMaster = "ItemCategory" });
        }

        try
        {
            await _repository.DeleteItemCategoryAsync(DeleteCode);
            TempData["SuccessMessage"] = "カテゴリを削除しました。";
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "カテゴリの削除に失敗しました。";
        }

        return RedirectToPage(new { SelectedMaster = "ItemCategory" });
    }
}