#pragma warning disable OPENAI001
using Azure;
using Azure.AI.OpenAI;
using HWDPortalMaui.Components.Layout;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using OpenAI.Assistants;
using OpenAI.Files;
using OpenAI.VectorStores;
using System.Text;

namespace HWDPortalMaui.Services
{
    // [新增] 將整個 AI 助理的邏輯和狀態封裝成一個單例服務
    public class AIAssistantService
    {
        // [新增] 用於通知 Blazor 元件狀態已變更的事件
        public event Action? OnChange;

        // [新增] 將所有與 AI 助理相關的欄位和屬性從 Razor 元件移至此處
        #region Public State Properties
        public List<ChatMessage> ChatMessages { get; } = new List<ChatMessage>();
        public string UserMessageInput { get; set; } = string.Empty;
        public bool IsLoading { get; private set; } = false;
        public bool IsInitializing { get; private set; } = true;
        public bool ShowSettingsModal { get; private set; } = false;
        public AssistantSettingsModel CurrentSettings { get; private set; } = new();
        public List<AssistantFileInfo> FilesInVectorStore { get; } = new();
        public bool ShowNewChatConfirm { get; private set; } = false;
        public string? NotificationMessage { get; private set; }
        public string NotificationCssClass { get; private set; } = "info";
        #endregion

        #region Private Fields
        private readonly UserInfoService _userInfoService;
        private string? userVectorStoreId;
        private bool isUsingCommonVector;

        // [新增] 常數與設定值
        private const string CommonVectorStoreId = "vs_PvsB5mOj3lbX5avtB13Ol2hw";
        private const string EmptyVectorStoreId = "vs_FVwAEhQWfk98hgZ9GmFE1LWK";
        private const string OpenAI_ApiKey = "d506304e02094ab0b0a0a95d5c4b268f";
        private const string OpenAI_IndexModel = "gpt-4.1-mini";
        private const string OpenAI_Uri = "https://ved1openai1.openai.azure.com/";

        // [新增] OpenAI 相關客戶端物件
        private AzureOpenAIClient? azureClient;
        private AssistantClient? assistantClient;
        private AssistantThread? thread;
        private Assistant? assistant;
        private RunCreationOptions? runOptions;
        #endregion

        // [新增] 服務的建構函式，用於注入依賴項
        public AIAssistantService(UserInfoService userInfoService)
        {
            _userInfoService = userInfoService;
        }

        // [新增] 通知 UI 更新的方法
        private void NotifyStateChanged() => OnChange?.Invoke();

        #region Public Methods (Previously in Razor Component)
        public async Task InitializeAssistantAsync()
        {
            Console.WriteLine("[Debug] AIAssistantService: 開始初始化助理...");
            ChatMessages.Add(new ChatMessage { Author = "HWD AI", Content = "Compal Electronics, Inc.<br>Hardware Division /Automation Team<br>=================================" });

            try
            {
                string? userName = _userInfoService.UserName;
                if (string.IsNullOrEmpty(userName))
                {
                    await ShowNotification("無法取得使用者資訊，助理初始化失敗。", "danger");
                    Console.WriteLine("[Debug] AIAssistantService: UserInfo.UserName 為空，中止初始化。");
                    return;
                }

                string assistantName = "Assistant_" + userName;
                string vectorStoreName = "Vector_" + userName;

                azureClient = new AzureOpenAIClient(new Uri(OpenAI_Uri), new AzureKeyCredential(OpenAI_ApiKey));
                assistantClient = azureClient.GetAssistantClient();
                var vectorStoreClient = azureClient.GetVectorStoreClient();

                bool vectorStoreExists = false;
                await foreach (var vectorStore in vectorStoreClient.GetVectorStoresAsync())
                {
                    if (vectorStore.Name == vectorStoreName)
                    {
                        if (vectorStore.Status.ToString() == "Expired")
                        {
                            await vectorStoreClient.DeleteVectorStoreAsync(vectorStore.Id);
                        }
                        else
                        {
                            vectorStoreExists = true;
                            userVectorStoreId = vectorStore.Id;
                            Console.WriteLine($"[Debug] 找到使用者向量存放區, ID: {userVectorStoreId}");
                        }
                        break;
                    }
                }

                if (!vectorStoreExists)
                {
                    var creationResult = await vectorStoreClient.CreateVectorStoreAsync(new VectorStoreCreationOptions { Name = vectorStoreName });
                    userVectorStoreId = creationResult.Value.Id;
                    Console.WriteLine($"[Debug] 建立新的使用者向量存放區, ID: {userVectorStoreId}");
                }

                string? assistantId = null;
                await foreach (Assistant assistantPageItem in assistantClient.GetAssistantsAsync())
                {
                    if (assistantPageItem.Name == assistantName)
                    {
                        assistantId = assistantPageItem.Id;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(assistantId))
                {
                    Console.WriteLine($"[Debug] 找到已存在的助理, ID: {assistantId}, 正在更新...");
                    var modificationOptions = new AssistantModificationOptions
                    {
                        Model = OpenAI_IndexModel,
                        Instructions = "1.保持禮貌\r\n2.使用表格呈現資料\r\n3.僅回答最近一次的問題\r\n4.預設使用繁體中文回答",
                        Temperature = 0.2f,
                        ToolResources = new() { FileSearch = new() { VectorStoreIds = { userVectorStoreId } } }
                    };
                    assistant = (await assistantClient.ModifyAssistantAsync(assistantId, modificationOptions)).Value;
                }
                else
                {
                    var creationOptions = new AssistantCreationOptions
                    {
                        Name = assistantName,
                        Instructions = "1.保持禮貌\r\n2.使用表格呈現資料\r\n3.僅回答最近一次的問題\r\n4.預設使用繁體中文回答",
                        Temperature = 0.2f,
                        ToolResources = new() { FileSearch = new() { VectorStoreIds = { userVectorStoreId } } }
                    };
                    creationOptions.Tools.Add(new FileSearchToolDefinition());
                    assistant = (await assistantClient.CreateAssistantAsync(OpenAI_IndexModel, creationOptions)).Value;
                    Console.WriteLine($"[Debug] 建立新的助理, ID: {assistant.Id}");
                }

                thread = (await assistantClient.CreateThreadAsync(new ThreadCreationOptions()
                {
                    ToolResources = new() { FileSearch = new() { VectorStoreIds = { CommonVectorStoreId } } }
                })).Value;
                isUsingCommonVector = true;
                Console.WriteLine($"[Debug] 已建立新的對話線程(Thread), ID: {thread.Id}, 並指向公用向量存放區。");

                runOptions = new RunCreationOptions
                {
                    AdditionalInstructions = "When possible, create table from data I ask for. Ensure columns represent distinct categories or data points, making the table clear and easy to understand."
                };
                Console.WriteLine("[Debug] AIAssistantService: 助理初始化成功。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] AIAssistantService: 發生錯誤: {ex.Message}");
                ChatMessages.Add(new ChatMessage { Author = "Error", Content = $"助理初始化失敗: {ex.Message}" });
            }
            finally
            {
                IsInitializing = false;
                NotifyStateChanged();
            }
        }

        public async Task HandleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !e.ShiftKey)
            {
                await SendMessageAsync();
            }
        }

        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(UserMessageInput) || IsLoading)
            {
                return;
            }

            Console.WriteLine("[Debug] SendMessageAsync: 開始發送訊息...");
            IsLoading = true;
            NotifyStateChanged();

            var userMessage = UserMessageInput.Trim();
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                UserMessageInput = string.Empty;
                return;
            }
            UserMessageInput = string.Empty;
            ChatMessages.Add(new ChatMessage { Author = "User", Content = userMessage });
            NotifyStateChanged();

            await StreamingChat(userMessage);

            IsLoading = false;
            NotifyStateChanged();
            Console.WriteLine("[Debug] SendMessageAsync: 訊息處理完成。");
        }

        public async Task StartNewChatAsync()
        {
            ShowNewChatConfirm = true;
            NotifyStateChanged();
            await Task.CompletedTask;
        }

        public async Task ConfirmNewChatAsync()
        {
            ShowNewChatConfirm = false;
            IsLoading = true;
            NotifyStateChanged();

            ChatMessages.Clear();
            if (thread != null && assistantClient != null)
            {
                Console.WriteLine($"[Debug] StartNewChatAsync: 刪除舊對話線程(Thread)，ID: {thread.Id}");
                await assistantClient.DeleteThreadAsync(thread.Id);
            }

            ChatMessages.Add(new ChatMessage { Author = "HWD AI", Content = "Compal Electronics, Inc.<br>Hardware Division /Automation Team<br>=================================" });
            if (assistantClient != null)
            {
                thread = await assistantClient.CreateThreadAsync();
                Console.WriteLine($"[Debug] StartNewChatAsync: 建立新的對話線程(Thread)，ID: {thread.Id}");
            }

            IsLoading = false;
            NotifyStateChanged();
        }

        public void CancelNewChat()
        {
            ShowNewChatConfirm = false;
            NotifyStateChanged();
        }

        public async Task OpenSettingsAsync()
        {
            Console.WriteLine("[Debug] OpenSettingsAsync: 使用者點擊助理設定。");
            if (assistant == null || assistantClient == null || azureClient == null)
            {
                await ShowNotification("助理尚未初始化，無法開啟設定。", "danger");
                return;
            }

            IsInitializing = true;
            NotifyStateChanged();
            try
            {
                var currentAssistantResult = await assistantClient.GetAssistantAsync(assistant.Id);
                var currentAssistant = currentAssistantResult.Value;

                CurrentSettings = new AssistantSettingsModel
                {
                    AssistantName = currentAssistant.Name,
                    Model = currentAssistant.Model,
                    Instructions = currentAssistant.Instructions,
                    Temperature = currentAssistant.Temperature ?? 0.2f,
                    UseCommonVectorStore = isUsingCommonVector
                };
                var userVsId = currentAssistant.ToolResources?.FileSearch?.VectorStoreIds.FirstOrDefault();
                if (!string.IsNullOrEmpty(userVsId))
                {
                    var vectorStoreClient = azureClient.GetVectorStoreClient();
                    await foreach (var vectorStore in vectorStoreClient.GetVectorStoresAsync())
                    {
                        if (vectorStore.Id == userVsId)
                        {
                            CurrentSettings.VectorStoreName = vectorStore.Name;
                            break;
                        }
                    }
                }
                else
                {
                    CurrentSettings.VectorStoreName = "個人資料庫 (未連結)";
                }

                Console.WriteLine($"[Debug] 正在為設定介面載入使用者向量存放區 '{userVectorStoreId}' 的檔案...");
                await LoadFilesInVectorStoreAsync(userVectorStoreId);
                ShowSettingsModal = true;
            }
            catch (Exception ex)
            {
                await ShowNotification($"載入助理設定失敗: {ex.Message}", "danger");
                Console.WriteLine($"[Debug] OpenSettingsAsync: 載入設定時發生錯誤: {ex.Message}");
            }
            finally
            {
                IsInitializing = false;
                NotifyStateChanged();
            }
        }

        public async Task HandleFileSelected(InputFileChangeEventArgs e)
        {
            if (assistantClient == null || azureClient == null || string.IsNullOrEmpty(userVectorStoreId))
            {
                await ShowNotification("向量存放區尚未準備就緒，無法上傳檔案。", "warning");
                return;
            }

            IsLoading = true;
            NotifyStateChanged();

            var file = e.File;
            try
            {
                await using var memoryStream = new MemoryStream();
                await file.OpenReadStream(maxAllowedSize: 200 * 1024 * 1024).CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                var fileClient = azureClient.GetOpenAIFileClient();
                var uploadResult = await fileClient.UploadFileAsync(memoryStream, file.Name, FileUploadPurpose.Assistants);

                if (uploadResult.Value != null)
                {
                    string fileId = uploadResult.Value.Id;
                    var vectorStoreClient = azureClient.GetVectorStoreClient();

                    // 這是正確的邏輯：將上傳的檔案 ID 加入到使用者的向量存放區
                    await vectorStoreClient.AddFileToVectorStoreAsync(userVectorStoreId, fileId);

                    await ShowNotification($"檔案 '{file.Name}' 已成功上傳並加入向量存放區。", "success");
                    await LoadFilesInVectorStoreAsync(userVectorStoreId);
                }
            }
            catch (Exception ex)
            {
                await ShowNotification($"檔案上傳失敗: {ex.Message}", "danger");
                Console.WriteLine($"[Debug] HandleFileSelected: 上傳失敗: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                NotifyStateChanged();
            }
        }

        public async Task DeleteFileAsync(string fileId)
        {
            IsLoading = true;
            NotifyStateChanged();

            try
            {
                // To properly delete, we need to remove from vector store first, then delete the file.
                var vectorStoreClient = azureClient!.GetVectorStoreClient();
                await vectorStoreClient.RemoveFileFromVectorStoreAsync(userVectorStoreId, fileId);

                var fileClient = azureClient.GetOpenAIFileClient();
                var deleteResult = await fileClient.DeleteFileAsync(fileId);

                if (deleteResult.Value != null && deleteResult.Value.Deleted)
                {
                    await ShowNotification("檔案已成功刪除。", "success");
                    await LoadFilesInVectorStoreAsync(userVectorStoreId);
                }
                else
                {
                    await ShowNotification("檔案刪除失敗。", "danger");
                }
            }
            catch (Exception ex)
            {
                await ShowNotification($"刪除檔案時發生錯誤: {ex.Message}", "danger");
                Console.WriteLine($"[Debug] DeleteFileAsync: 刪除失敗: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                NotifyStateChanged();
            }
        }

        public async Task SaveSettingsAsync()
        {
            if (assistantClient == null || assistant == null || thread == null) return;
            IsLoading = true;
            NotifyStateChanged();

            try
            {
                var assistantOptions = new AssistantModificationOptions
                {
                    Instructions = CurrentSettings.Instructions,
                    Temperature = CurrentSettings.Temperature
                };
                var updatedAssistantResult = await assistantClient.ModifyAssistantAsync(assistant.Id, assistantOptions);
                assistant = updatedAssistantResult.Value;
                Console.WriteLine("[Debug] 助理的 Instructions 和 Temperature 已更新。");

                if (CurrentSettings.UseCommonVectorStore != isUsingCommonVector)
                {
                    string targetVectorStoreId = CurrentSettings.UseCommonVectorStore ? CommonVectorStoreId : (userVectorStoreId ?? EmptyVectorStoreId);
                    string targetName = CurrentSettings.UseCommonVectorStore ? "公用資料庫" : "個人資料庫";

                    Console.WriteLine($"[Debug] 向量存放區開關已變更。正在將執行緒 '{thread.Id}' 指向 '{targetName}' ({targetVectorStoreId})。");
                    var threadOptions = new ThreadModificationOptions
                    {
                        ToolResources = new() { FileSearch = new() { VectorStoreIds = { targetVectorStoreId } } }
                    };
                    await assistantClient.ModifyThreadAsync(thread.Id, threadOptions);
                    isUsingCommonVector = CurrentSettings.UseCommonVectorStore;
                }

                await ShowNotification("助理設定已成功儲存。", "success");
                ShowSettingsModal = false;
            }
            catch (Exception ex)
            {
                await ShowNotification($"儲存設定失敗: {ex.Message}", "danger");
                Console.WriteLine($"[Debug] SaveSettingsAsync: 儲存失敗: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                NotifyStateChanged();
            }
        }

        public async Task CloseSettingsModalAsync()
        {
            ShowSettingsModal = false;
            NotifyStateChanged();
            await Task.CompletedTask;
        }

        #endregion

        #region Private Helper Methods
        private async Task StreamingChat(string userMessage)
        {
            if (assistantClient == null || thread == null || assistant == null || runOptions == null)
            {
                ChatMessages.Add(new ChatMessage { Author = "Error", Content = "助理尚未準備就緒。" });
                return;
            }

            try
            {
                Console.WriteLine($"[Debug] StreamingChat: 對使用者訊息 '{userMessage}' 建立 Message...");
                await assistantClient.CreateMessageAsync(thread.Id, MessageRole.User, new[] { MessageContent.FromText(userMessage) });
                var aiResponseBuilder = new StringBuilder();
                var currentAIMessage = new ChatMessage { Author = "HWD AI", Content = "" };
                ChatMessages.Add(currentAIMessage);
                Console.WriteLine("[Debug] StreamingChat: 開始建立 Run 並接收串流回應...");

                await foreach (StreamingUpdate streamingUpdate in assistantClient.CreateRunStreamingAsync(thread.Id, assistant.Id, runOptions))
                {
                    if (streamingUpdate is MessageContentUpdate contentUpdate && contentUpdate.Text != null)
                    {
                        aiResponseBuilder.Append(contentUpdate.Text);
                        currentAIMessage.Content = aiResponseBuilder.ToString().Replace("\n", "<br>");
                        NotifyStateChanged();
                    }
                }
                Console.WriteLine($"[Debug] StreamingChat: 串流接收完成，AI 最終回覆: {aiResponseBuilder.ToString()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Debug] StreamingChat: 發生錯誤: {ex.Message}");
                ChatMessages.Add(new ChatMessage { Author = "Error", Content = $"訊息處理失敗: {ex.Message}" });
            }
        }

        // 修正 LoadFilesInVectorStoreAsync 方法以解決 CS1061 錯誤
        private async Task LoadFilesInVectorStoreAsync(string? vectorStoreId)
        {
            FilesInVectorStore.Clear();
            if (string.IsNullOrEmpty(vectorStoreId) || assistantClient == null || azureClient == null)
            {
                return;
            }

            var vectorStoreClient = azureClient.GetVectorStoreClient();
            var fileClient = azureClient.GetOpenAIFileClient();

            var associatedFiles = new List<VectorStoreFile>();
            await foreach (var vectorStoreFile in vectorStoreClient.GetVectorStoreFilesAsync(vectorStoreId))
            {
                associatedFiles.Add(vectorStoreFile);
            }

            foreach (var file in associatedFiles) // 'file' is of type VectorStoreFile
            {
                try
                {
                    // 為了取得檔名，仍然需要查詢 File API
                    // 將 file.Id 修正為正確的屬性 file.FileId
                    var fileDetails = await fileClient.GetFileAsync(file.FileId);
                    if (fileDetails.Value != null)
                    {
                        // 將 file.Id 修正為正確的屬性 file.FileId
                        FilesInVectorStore.Add(new AssistantFileInfo { Id = file.FileId, Name = fileDetails.Value.Filename });
                    }
                }
                catch (Exception ex)
                {
                    // 將 file.Id 修正為正確的屬性 file.FileId
                    Console.WriteLine($"[Debug] 無法獲取檔案 {file.FileId} 的詳細資訊: {ex.Message}");
                }
            }
        }

        private async Task ShowNotification(string message, string type = "success", int delay = 3000)
        {
            NotificationMessage = message;
            NotificationCssClass = $"notification-{type}";
            NotifyStateChanged();
            await Task.Delay(delay);
            NotificationMessage = null;
            NotifyStateChanged();
        }

        #endregion

        #region Helper Classes (Previously in Razor Component)
        public class ChatMessage
        {
            public string Author { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
        }

        public class AssistantFileInfo
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        public class AssistantSettingsModel
        {
            public string? AssistantName { get; set; }
            public string? Model { get; set; }
            public string? Instructions { get; set; }
            public float Temperature { get; set; }
            public string? VectorStoreName { get; set; }
            public bool UseCommonVectorStore { get; set; }
        }
        #endregion
    }
}