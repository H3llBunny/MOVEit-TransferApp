using MOVEit_TransferApp.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;

namespace MOVEit_TransferApp.Services
{
    public class FolderWatchService : IDisposable
    {
        private FileSystemWatcher _watcher;
        private readonly HttpClient _client;
        private readonly ILogger<FolderWatchService> _logger;
        private string _tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");
        private string _userFolderPath = Path.Combine(AppContext.BaseDirectory, "user_folder_path.txt");
        private const string _url = "https://testserver.moveitcloud.com/api/v1/folders";
        private const int _MaxFileSize = 52428800;

        public FolderWatchService(HttpClient client, ILogger<FolderWatchService> logger)
        {
            _client = client;
            _logger = logger;
            _client.Timeout = TimeSpan.FromMinutes(30);
            _client.DefaultRequestHeaders.ConnectionClose = false;
        }

        public async void MonitorFolder(string folderPath, Func<string, int, Task> notificationCallBack = null)
        {
            StopWatching();

            await File.WriteAllTextAsync(_userFolderPath, folderPath);

            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Created += (sender, e) => Task.Run(() => UploadNewFileAsync(e.FullPath, notificationCallBack));
        }

        private async Task UploadNewFileAsync(string filePath, Func<string, int, Task> notificationCallBack = null)
        {
            string homeFolderId = await GetHomeFolderId();

            if (homeFolderId != null)
            {
                var fileInfo = new FileInfo(filePath);
                long fileSize = fileInfo.Length;
                string token = await GetUserToken();

                if (fileSize > _MaxFileSize)
                {
                    try
                    {
                        await UploadFileInChunksAsync(fileInfo, filePath, fileSize, homeFolderId, token, notificationCallBack);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
                else
                {
                    using var fileStream = File.OpenRead(filePath);
                    var fileContent = new StreamContent(fileStream);

                    var content = new MultipartFormDataContent()
                    {
                        { fileContent, "file", Path.GetFileName(filePath) }
                    };

                    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var response = await _client.PostAsync($"{_url}/{homeFolderId}/files", content);

                    if (response.IsSuccessStatusCode)
                    {
                        notificationCallBack?.Invoke(fileInfo.Name, (int)fileSize);
                    }
                    else
                    {
                        string errorMessage = $"{fileInfo.Name} - {response.StatusCode}";
                        notificationCallBack?.Invoke(errorMessage, -1);
                        _logger.LogError($"Error uploading file: {response.StatusCode}");
                    }
                }
            }
            else
            {
                _logger.LogError("Home folder ID not found. Unable to upload the file.");
            }
        }

        private async Task UploadFileInChunksAsync(FileInfo fileInfo, string filePath, long fileSize,
            string homeFolderId, string token, Func<string, int, Task> notificationCallBack = null)
        {
            string fileName = fileInfo.Name;
            string hashString = GetHashString(filePath);

            var requestUri = $"{_url}/{homeFolderId}/files";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TransferEncodingChunked = true;

            try
            {
                request.Content = new MultipartChunkedFileContent(filePath, fileName, hashString);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                notificationCallBack?.Invoke(fileName, (int)fileSize);
            }
            else
            {
                string errorMessage = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"File upload failed: {response.StatusCode}");
                Console.WriteLine($"Error details: {errorMessage}");
            }
        }

        private static string GetHashString(string filePath)
        {
            using var hashAlgorithm = SHA256.Create();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            byte[] hashBytes = hashAlgorithm.ComputeHash(fileStream);

            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        }

        private async Task<string> GetHomeFolderId()
        {
            string token = await GetUserToken();
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _client.GetAsync("https://testserver.moveitcloud.com/api/v1/users/self");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(content);

                if (jsonDoc.RootElement.TryGetProperty("homeFolderID", out var homeFolderIdElement))
                {
                    long homeFolderIdLong = homeFolderIdElement.GetInt64();
                    string homeFolderId = homeFolderIdLong.ToString();
                    return homeFolderId;
                }
                else
                {
                    _logger.LogError("The 'homeFolderID' property is missing in the response.");
                    return null;
                }
            }
            else
            {
                _logger.LogError("Error while trying to obtain homeFolderID: " + response.StatusCode);
                return null;
            }
        }

        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }

        private async Task<string> GetUserToken()
        {
            var tokenJson = await File.ReadAllTextAsync(_tokenPath);

            var tokenModel = JsonSerializer.Deserialize<Token>(tokenJson);
            return tokenModel.AccessToken;
        }

        public void Dispose()
        {
            StopWatching();
        }
    }
}
