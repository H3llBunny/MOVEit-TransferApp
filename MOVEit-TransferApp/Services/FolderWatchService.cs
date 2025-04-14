using MOVEit_TransferApp.Models;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Cryptography;
using Serilog.Sinks.File;
using System.IO;

namespace MOVEit_TransferApp.Services
{
    public class FolderWatchService : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private readonly HttpClient _client;
        private readonly ILogger<FolderWatchService> _logger;
        private string _tokenPath = Path.Combine(AppContext.BaseDirectory, "user_token.json");
        private string _userFolderPath = Path.Combine(AppContext.BaseDirectory, "user_folder_path.txt");
        private const string _url = "https://testserver.moveitcloud.com/api/v1/folders";
        private const string _deleteUrl = "https://testserver.moveitcloud.com/api/v1/files/";
        private const int _MaxFileSize = 52428800;

        public FolderWatchService(HttpClient client, ILogger<FolderWatchService> logger)
        {
            _client = client;
            _logger = logger;
            _client.Timeout = TimeSpan.FromMinutes(30);
            _client.DefaultRequestHeaders.ConnectionClose = false;
        }

        public async void MonitorFolder(string folderPath, Func<string, long, Task> notificationCallBack = null)
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
            _watcher.Deleted += (sender, e) => Task.Run(() => DeleteFileAsync(e.FullPath, notificationCallBack));
        }

        private async Task UploadNewFileAsync(string filePath, Func<string, long, Task>? notificationCallBack = null)
        {
            if (!await WaitForFile(filePath))
            {
                Console.WriteLine($"File {filePath} is not accessible.");
                return;
            }

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
                    using var fileContent = new StreamContent(fileStream);

                    var content = new MultipartFormDataContent()
                    {
                        { fileContent, "file", Path.GetFileName(filePath) }
                    };

                    _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var response = await _client.PostAsync($"{_url}/{homeFolderId}/files", content);

                    if (response.IsSuccessStatusCode)
                    {
                        notificationCallBack?.Invoke(fileInfo.Name, fileSize);
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

        private static async Task<bool> WaitForFile(string filePath, int delayMs = 3000, int maxAttempts = 100)
        {
            int attempt = 1;

            while (true)
            {
                try
                {
                    using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.None);

                    attempt++;

                    if (stream.Length > 0)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    Console.WriteLine($"File {filePath} is still being written... Retrying {attempt}/{maxAttempts}");

                    if (attempt >= maxAttempts)
                    {
                        return false;
                    }
                }

                await Task.Delay(delayMs);
            }
        }

        private async Task UploadFileInChunksAsync(FileInfo fileInfo, string filePath, long fileSize,
            string homeFolderId, string token, Func<string, long, Task>? notificationCallBack = null)
        {
            string fileName = fileInfo.Name;

            var requestUri = $"{_url}/{homeFolderId}/files";

            var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TransferEncodingChunked = true;

            try
            {
                request.Content = new MultipartChunkedFileContent(filePath, fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                notificationCallBack?.Invoke(fileName, fileSize);
            }
            else
            {
                string errorMessage = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"File upload failed: {response.StatusCode}");
                Console.WriteLine($"Error details: {errorMessage}");
            }
        }

        private async Task DeleteFileAsync(string filePath, Func<string, long, Task>? notificationCallBack = null)
        {
            string? homeFolderId = await GetHomeFolderId();
            string? token = await GetUserToken();
            var fileInfo = new FileInfo(filePath);
            long fileSize = filePath.Length;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(homeFolderId)) return;

            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            string filesUrl = $"{_url}/{homeFolderId}/files";
            var filesResponse = await _client.GetAsync(filesUrl);

            if (filesResponse.IsSuccessStatusCode)
            {
                var filesContent = await filesResponse.Content.ReadAsStringAsync();
                var jsonDoc = JsonDocument.Parse(filesContent);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                {
                    string localFileName = Path.GetFileName(filePath);

                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("name", out var nameProperty) && nameProperty.GetString() == localFileName)
                        {
                            var itemId = item.GetProperty("id").GetString();
                            string deleteUrl = $"{_deleteUrl}{itemId}";
                            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                            var response = await _client.DeleteAsync(deleteUrl);

                            if (response.IsSuccessStatusCode)
                            {
                                notificationCallBack?.Invoke(localFileName, -2);
                            }
                            else
                            {
                                string errorMessage = $"{localFileName} - {response.StatusCode}";
                                notificationCallBack?.Invoke(errorMessage, -1);
                                _logger.LogError($"Error uploading file: {response.StatusCode}");
                            }
                        }
                    }
                }
            }
        }

        private async Task<string?> GetHomeFolderId()
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
