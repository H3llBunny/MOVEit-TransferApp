﻿using Microsoft.Extensions.Primitives;
using MOVEit_TransferApp.Models;
using System.Net.Http.Headers;
using System.Text.Json;

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

        public FolderWatchService(HttpClient client, ILogger<FolderWatchService> logger)
        {
            _client = client;
            _logger = logger;
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
                using var fileStream = File.OpenRead(filePath);
                var fileContent = new StreamContent(fileStream);

                var content = new MultipartFormDataContent();
                content.Add(fileContent, "file", Path.GetFileName(filePath));

                string token = await GetUserToken();

                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _client.PostAsync($"{_url}/{homeFolderId}/files", content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine(await response.Content.ReadAsStringAsync());

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(responseContent);

                    if (jsonDoc.RootElement.TryGetProperty("name", out var fileName) && jsonDoc.RootElement.TryGetProperty("size", out var size))
                    {
                        string fileNameString = fileName.GetString();
                        int sizeInt = size.GetInt32();
                        notificationCallBack?.Invoke(fileNameString, sizeInt);
                    }
                }
                else
                {
                    string fileName = Path.GetFileName(filePath);
                    string errorMessage = $"{fileName} - {response.StatusCode}";
                    notificationCallBack?.Invoke(errorMessage, -1);
                    _logger.LogError($"Error uploading file: {response.StatusCode}");
                }
            }
            else
            {
                _logger.LogError("Home folder ID not found. Unable to upload the file.");
            }
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
