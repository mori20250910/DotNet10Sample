using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Linq;

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

    [BindProperty(SupportsGet = true)]
    public string? ReturnItemName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnItemCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnCategoryCode { get; set; }

    public ItemRepository.Item? Item { get; private set; }

    [BindProperty]
    public string ItemName { get; set; } = string.Empty;

    [BindProperty]
    public string ItemCode { get; set; } = string.Empty;

    [BindProperty]
    public string? CategoryCode { get; set; }

    [BindProperty]
    public string? Remarks { get; set; }

    [BindProperty]
    public DateTime? ManufactureStartDate { get; set; }

    public List<ItemRepository.ItemCategory> Categories { get; private set; } = new();

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

            // 初期表示時は ItemName 等に現在の品目情報を設定
            ItemCode = Item.Code;
            ItemName = Item.Name;
            CategoryCode = Item.CategoryCode;
            Remarks = Item.Remarks;
            ManufactureStartDate = Item.ManufactureStartDate;

            Categories = await _repository.GetItemCategoriesAsync();
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "詳細情報の取得に失敗しました。接続設定やSQL Serverの状態を確認してください。";
            return RedirectToPage("/Search");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateAsync()
    {
        if (Id <= 0)
        {
            ModelState.AddModelError(string.Empty, "無効なIDです。");
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, errors = new[] { "無効なIDです。" } });
            }

            TempData["ErrorMessage"] = "無効なIDです。";
            return Page();
        }

        // Ensure item exists and lock ItemCode to the database value (ItemCode is not editable on the detail page)
        Item = await _repository.GetByIdAsync(Id);
        if (Item == null)
        {
            ModelState.AddModelError(string.Empty, "指定された品目が見つかりませんでした。");
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, errors = new[] { "指定された品目が見つかりませんでした。" } });
            }

            TempData["ErrorMessage"] = "指定された品目が見つかりませんでした。";
            return RedirectToPage("/Search");
        }

        // Ignore any ItemCode value posted by client and use the DB value
        ItemCode = Item.Code;

        // Validate ItemName
        if (string.IsNullOrWhiteSpace(ItemName))
        {
            ModelState.AddModelError(nameof(ItemName), "品目名を入力してください。");
            Item = await _repository.GetByIdAsync(Id);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var errors = ModelState[nameof(ItemName)]?.Errors.Select(e => e.ErrorMessage).ToArray() ?? new[] { "品目名を入力してください。" };
                return BadRequest(new { success = false, errors });
            }

            return Page();
        }

        if (ItemName.Length > 10)
        {
            ModelState.AddModelError(nameof(ItemName), "品目名は10文字以内で入力してください。");
            Item = await _repository.GetByIdAsync(Id);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var errors = ModelState[nameof(ItemName)]?.Errors.Select(e => e.ErrorMessage).ToArray() ?? new[] { "品目名は10文字以内で入力してください。" };
                return BadRequest(new { success = false, errors });
            }

            return Page();
        }

        // Validate Remarks
        if (!string.IsNullOrWhiteSpace(Remarks) && Remarks.Length > 100)
        {
            ModelState.AddModelError(nameof(Remarks), "備考は100文字以内で入力してください。");
            Item = await _repository.GetByIdAsync(Id);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                var errors = ModelState[nameof(Remarks)]?.Errors.Select(e => e.ErrorMessage).ToArray() ?? new[] { "備考は100文字以内で入力してください。" };
                return BadRequest(new { success = false, errors });
            }

            return Page();
        }

        try
        {
            await _repository.UpdateAsync(Id, ItemCode, ItemName, string.IsNullOrWhiteSpace(CategoryCode) ? null : CategoryCode, string.IsNullOrWhiteSpace(Remarks) ? null : Remarks, ManufactureStartDate);

            // 更新後の表示用に最新情報を取得
            Item = await _repository.GetByIdAsync(Id);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return new JsonResult(new { success = true, message = "品目を更新しました。", code = Item?.Code, name = Item?.Name, category = Item?.CategoryCode, remarks = Item?.Remarks, manufactureStartDate = Item?.ManufactureStartDate.HasValue == true ? Item?.ManufactureStartDate.Value.ToString("yyyy-MM-dd") : null });
            }

            TempData["SuccessMessage"] = "品目を更新しました。";
            return Page();
        }
        catch (InvalidOperationException ex)
        {
            // uniqueness error
            ModelState.AddModelError(string.Empty, ex.Message);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return BadRequest(new { success = false, errors = new[] { ex.Message } });
            }

            Item = await _repository.GetByIdAsync(Id);
            return Page();
        }
        catch (Exception)
        {
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return StatusCode(500, new { success = false, errors = new[] { "更新に失敗しました。" } });
            }

            TempData["ErrorMessage"] = "更新に失敗しました。";
            return Page();
        }
    }
}


