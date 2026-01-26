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

    public string? ResultMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var id = await _repository.InsertAsync(Input.ItemName);
            ResultMessage = $"登録しました (ID: {id})";
            ModelState.Clear();
            Input = new ItemInput();
        }
        catch (Exception)
        {
            ModelState.AddModelError(string.Empty, "登録に失敗しました。接続設定やSQL Serverの状態を確認してください。");
        }

        return Page();
    }

    public class ItemInput
    {
        [Required(ErrorMessage = "品目名を入力してください。")]
        [StringLength(10, ErrorMessage = "品目名は10文字以内で入力してください。")]
        [Display(Name = "品目名")]
        public string ItemName { get; set; } = string.Empty;
    }
}


