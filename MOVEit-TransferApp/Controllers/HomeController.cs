using Microsoft.AspNetCore.Mvc;
using MOVEit_TransferApp.Models;
using MOVEit_TransferApp.Services;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using MOVEit_TransferApp.Hubs;

namespace MOVEit_TransferApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ITokenService _tokenService;
        private readonly FolderWatchService _folderWatchService;
        private readonly IHubContext<UploadNotificationHub> _hubContext;

        public HomeController(ITokenService tokenService, FolderWatchService folderWatchService, IHubContext<UploadNotificationHub> hubContext)
        {
            _tokenService = tokenService;
            _folderWatchService = folderWatchService;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            string tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");
            string userFolderPath = Path.Combine(AppContext.BaseDirectory, "user_folder_path.txt");

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
                    TempData["HasToken"] = true;
                    bool folderPath = System.IO.File.Exists(userFolderPath);
                    string path = null;

                    if (folderPath)
                    {
                        path = await System.IO.File.ReadAllTextAsync(userFolderPath);

                        _folderWatchService.MonitorFolder(path, async (fileName, size) =>
                        {
                            var connectionId = UploadNotificationHub.GetConnectionId();
                            if (connectionId != null)
                            {
                                await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveNotification", fileName, size);
                            }
                        });
                    }

                    var viewModel = new HomePageViewModel { HasToken = true, UserFolderPath = folderPath ? path : null };
                    return View(viewModel);
                }
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetCredentials(string username, string password)
        {
            bool requestSuccess = await _tokenService.RequestTokenAsync(username, password);

            if (requestSuccess)
            {
                return RedirectToAction(nameof(Index));
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AddFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                TempData["ErrorMessage"] = "Please make sure you folder path is correct.";
                return RedirectToAction(nameof(Index));
            }

            _folderWatchService.MonitorFolder(folderPath, async (fileName, size) =>
            {
                var connectionId = UploadNotificationHub.GetConnectionId();
                if (connectionId != null)
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveNotification", fileName, size);
                }
            });

            return RedirectToAction(nameof(Index));
        }

        public IActionResult Logout()
        {
            string tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");
            string userFolderPath = Path.Combine(AppContext.BaseDirectory, "user_folder_path.txt");

            if (System.IO.File.Exists(tokenPath))
            {
                System.IO.File.Delete(tokenPath);
            }

            if (System.IO.File.Exists(userFolderPath))
            {
                System.IO.File.Delete(userFolderPath);
            }

            return RedirectToAction(nameof(Index));
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
