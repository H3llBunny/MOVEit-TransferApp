using Microsoft.AspNetCore.Mvc;
using MOVEit_TransferApp.Models;
using MOVEit_TransferApp.Services;
using System.Text.Json;
using System.Diagnostics;

namespace MOVEit_TransferApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ITokenService _tokenService;

        public HomeController(ITokenService tokenService)
        {
            this._tokenService = tokenService;
        }

        public async Task<IActionResult> Index()
        {
            string tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");

            if (System.IO.File.Exists(tokenPath))
            {
                string tokenJson = await System.IO.File.ReadAllTextAsync(tokenPath);
                var token = JsonSerializer.Deserialize<Token>(tokenJson);

                if (token.IsExpired())
                {
                    return View();
                }
                else
                {
                    var viewModel = new HomePageViewModel { HasToken = true };
                    return View(viewModel);
                }
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetCredentials (string username, string password)
        {
            bool requestSuccess = await _tokenService.RequestTokenAsync(username, password);

            if (requestSuccess)
            {
                return RedirectToAction(nameof(Index));
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
