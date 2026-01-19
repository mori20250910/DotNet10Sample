using System.ComponentModel.DataAnnotations;
using DotNet10Sample.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DotNet10Sample.Pages;

public class RegisterModel : PageModel
{
    private readonly ItemRepository _repository;

    public RegisterModel(ItemRepository repository)
    {
        _repository = repository;
    }

    [BindProperty]
    public ItemInput Input { get; set; } = new();

    public List<ItemRepository.ItemCategory> Categories { get; private set; } = new();

    public string? ResultMessage { get; private set; }

    public async Task OnGetAsync()
    {
        Categories = await _repository.GetItemCategoriesAsync();
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
            // additional server-side uniqueness check
            if (await _repository.ExistsItemCodeAsync(Input.ItemCode))
            {
                ModelState.AddModelError("Input.ItemCode", "品目コードは既に使用されています。");
                return Page();
            }

            var id = await _repository.InsertAsync(Input.ItemCode, Input.ItemName, string.IsNullOrWhiteSpace(Input.CategoryCode) ? null : Input.CategoryCode, string.IsNullOrWhiteSpace(Input.Remarks) ? null : Input.Remarks, Input.ManufactureStartDate);
            ResultMessage = $"登録しました (ID: {id})";
            ModelState.Clear();
            Input = new ItemInput();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "登録に失敗しました。接続設定やSQL Serverの状態を確認してください。");
        }

        return Page();
    }

    public class ItemInput
    {
        [Required(ErrorMessage = "品目コードを入力してください。")]
        [RegularExpression("^[0-9]{1,5}$", ErrorMessage = "品目コードは数字で最大5桁で入力してください。")]
        [Display(Name = "品目コード")]
        public string ItemCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "品目名を入力してください。")]
        [StringLength(10, ErrorMessage = "品目名は10文字以内で入力してください。")]
        [Display(Name = "品目名")]
        public string ItemName { get; set; } = string.Empty;

        [Display(Name = "品目カテゴリ")]
        public string? CategoryCode { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "製造開始年月日")]
        public DateTime? ManufactureStartDate { get; set; }

        [StringLength(100, ErrorMessage = "備考は100文字以内で入力してください。")]
        [Display(Name = "備考")]
        public string? Remarks { get; set; }
    }
}


