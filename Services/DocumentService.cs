using Microsoft.Extensions.Logging;
using System.IO;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Configuration;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Maui.Storage;

namespace HWDPortalMaui.Services
{
    // 將 DocumentItem 模型直接定義在此檔案中，以符合您的要求。
    /// <summary>
    /// 代表從指定目錄掃描到的單一文件項目。
    /// </summary>
    public class DocumentItem
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FileFullPath { get; set; } = string.Empty;
        public string Source { get; set; } = "N/A";
        public string Platform { get; set; } = "N/A";
        public string Version { get; set; } = "N/A";
        public int Year { get; set; }
        public DateTime UploadTime { get; set; }
    }


    /// <summary>
    /// 負責掃描並提供文件列表的服務。
    /// </summary>
    public class DocumentService
    {
        private readonly ILogger<DocumentService> _logger;
        // 用於讀取 appsettings.json
        private readonly IConfiguration _configuration;
        // 用於取得使用者資訊的服務
        private readonly UserInfoService _userInfoService;
        // 用於快取掃描結果，避免重複讀取磁碟
        private static List<DocumentItem>? _cachedDocuments;
        private static DateTime _lastCacheTime;
        // 快取有效時間設為 5 分鐘
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        // 用於防止多個執行緒同時更新快取的 SemaphoreSlim
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public DocumentService(ILogger<DocumentService> logger, IConfiguration configuration, UserInfoService userInfoService)
        {
            _logger = logger;
            _configuration = configuration;
            _userInfoService = userInfoService;
        }
        // 紀錄檔案操作 (上傳/刪除/開啟) 的私有方法
        private async Task LogActionAsync(string actionType, DocumentItem item)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
                using var connection = new SqlConnection(connectionString);
                // 根據 A31_HWD_Portal_Document_FileAction_Record.txt 的結構
                const string sql = @"
            INSERT INTO A31_HWD_Portal_Document_FileAction_Record 
            (Division, Department, UserName, Year, Source, Platform, Version, FileName, ActionType, ActionTime)
            VALUES 
            (@Division, @Department, @UserName, @Year, @Source, @Platform, @Version, @FileName, @ActionType, @ActionTime)";

                await connection.ExecuteAsync(sql, new
                {
                    Division = _userInfoService.UserDivision, // 從 UserInfoService 取得 Division
                    Department = _userInfoService.UserDepartment, // 從 UserInfoService 取得使用者部門
                    _userInfoService.UserName, // 從 UserInfoService 取得使用者名稱
                    item.Year,
                    item.Source,
                    item.Platform,
                    item.Version,
                    //FileName = item.FileFullPath, // 儲存實體檔案路徑 (對應 BulletinService 的邏輯)
                    FileName = item.FileName,
                    ActionType = actionType, // 操作類型 ('Upload', 'Delete', 'Open' 等)
                    ActionTime = DateTime.Now // 當前時間
                });
                // 使用 item.FileName (原始檔名) 來顯示日誌，較易閱讀
                _logger.LogInformation($"已成功紀錄文件操作: {actionType}, 使用者: {_userInfoService.UserName}, 檔案: {item.FileName}");
            }
            catch (Exception ex)
            {
                // 紀錄日誌失敗不應影響主要操作，所以只記錄錯誤訊息
                _logger.LogError(ex, "紀錄文件操作時發生錯誤。");
            }
        }

        public async Task<List<DocumentItem>> GetDocumentsAsync(bool forceReload = false)
        {
            // 快取檢查邏輯與 BulletinService 保持一致
            if (!forceReload && _cachedDocuments != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
            {
                _logger.LogInformation("從快取中讀取文件列表。");
                return _cachedDocuments;
            }

            // 使用 SemaphoreSlim 確保執行緒安全
            await _semaphore.WaitAsync();
            try
            {
                // 再次檢查快取，防止在等待時其他執行緒已更新
                if (!forceReload && _cachedDocuments != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
                {
                    return _cachedDocuments;
                }

                _logger.LogInformation("快取過期或被強制更新，正在從資料庫重新載入文件列表...");

                var items = new List<DocumentItem>();
                try
                {
                    // 從 appsettings.json 取得唯讀連線字串
                    var connectionString = _configuration.GetConnectionString("ReadConnection_A31_HWD");
                    using var connection = new SqlConnection(connectionString);

                    // 查詢 A31_HWD_Portal_Document 資料表的 SQL 語法
                    const string sql = @"
                SELECT Id, Year, Source, Platform, Version, FileName, FileFullPath, UploadTime 
                FROM A31_HWD_Portal_Document 
                ORDER BY UploadTime DESC";

                    var result = await connection.QueryAsync<DocumentItem>(sql);
                    items = result.ToList();

                    // 更新快取
                    _cachedDocuments = items;
                    _lastCacheTime = DateTime.UtcNow;

                    _logger.LogInformation("成功從資料庫掃描並快取了 {Count} 個文件。", items.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "從資料庫載入文件時發生錯誤。");
                    // 即使發生錯誤，也回傳一個空列表，避免頁面崩潰
                    _cachedDocuments = new List<DocumentItem>();
                    _lastCacheTime = DateTime.UtcNow;
                }

                return _cachedDocuments;
            }
            finally
            {
                // 釋放信號量
                _semaphore.Release();
            }
        }


        public async Task UploadDocumentAsync(int year, string source, string platform, string version, IBrowserFile file)
        {

            // 從 appsettings.json 讀取文件儲存根路徑，與 BulletinService 一致
            var storedFilesPath = _configuration.GetValue<string>("StoredDocumentFilesPath");
            if (string.IsNullOrEmpty(storedFilesPath))
            {
                throw new InvalidOperationException("appsettings.json 中未設定 StoredDocumentFilesPath。");
            }

            // 使用 MAUI 的 FileSystem.AppDataDirectory 取得平台通用的應用程式資料目錄
            var uploadPath = Path.Combine(FileSystem.AppDataDirectory, storedFilesPath);
            Directory.CreateDirectory(uploadPath); // 如果目錄不存在，就建立它

            // 為了避免檔名衝突，建立一個安全的新檔名
            var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.Name)}";
            var targetFilePath = Path.Combine(uploadPath, safeFileName);

            _logger.LogInformation("準備上傳文件至: {TargetFilePath}", targetFilePath);

            // [新增] 取得來源檔案的資料流，並使用 await using 確保它會被正確釋放
            await using var sourceStream = file.OpenReadStream(500 * 1024 * 1024);

            // 將上傳的檔案流寫入到目標路徑
            // fs (目標資料流) 也改用 await using
            await using (var fs = new FileStream(targetFilePath, FileMode.Create))
            {
                // 從已明確開啟的 sourceStream 複製到目標 FileStream
                await sourceStream.CopyToAsync(fs);
            }

            try
            {
                // 從 appsettings.json 取得寫入連線字串
                var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
                using var connection = new SqlConnection(connectionString);

                // 將檔案中繼資料寫入 A31_HWD_Portal_Document 資料表的 SQL 語法
                const string sql = @"
            INSERT INTO A31_HWD_Portal_Document 
            (Year, Source, Platform, Version, FileName, FileFullPath, UploadTime)
            VALUES 
            (@Year, @Source, @Platform, @Version, @FileName, @FileFullPath, @UploadTime)";

                await connection.ExecuteAsync(sql, new
                {
                    Year = year,
                    Source = source,
                    Platform = platform,
                    Version = version,
                    FileName = file.Name, // 儲存原始檔名
                    FileFullPath = targetFilePath, // 儲存包含安全檔名的完整實體路徑
                    UploadTime = DateTime.Now
                });

                _logger.LogInformation("文件 {FileName} 的資料庫紀錄新增成功。", file.Name);

                // 建立一個 DocumentItem 物件來傳遞給紀錄方法
                var uploadedItem = new DocumentItem
                {
                    Year = year,
                    Source = source,
                    Platform = platform,
                    Version = version,
                    FileName = file.Name, // 原始檔名
                    FileFullPath = targetFilePath // 實體路徑
                };
                // 呼叫紀錄方法
                await LogActionAsync("Upload", uploadedItem);

                // 操作成功後，清除快取，以便下次取得最新資料
                _cachedDocuments = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增文件紀錄到資料庫時發生錯誤。");
                // 如果資料庫寫入失敗，將剛才儲存的檔案刪除，以保持資料一致性
                if (File.Exists(targetFilePath))
                {
                    File.Delete(targetFilePath);
                    _logger.LogInformation("已刪除因資料庫錯誤而無法紀錄的檔案: {TargetFilePath}", targetFilePath);
                }
                // 拋出例外，讓前端知道操作失敗
                throw;
            }
        }
        /// <summary>
        /// 紀錄「開啟文件」的操作。
        /// </summary>
        /// <param name="item">被開啟的文件項目</param>
        public async Task LogOpenFileAsync(DocumentItem item)
        {
            // 直接呼叫內部的私有 LogActionAsync 方法
            await LogActionAsync("Open", item);
        }

        /// <summary>
        /// 刪除一個文件。
        /// </summary>
        /// <param name="item">要刪除的 DocumentItem 物件</param>
        // 整體邏輯重寫，以符合資料庫操作模式
        public async Task DeleteDocumentAsync(DocumentItem item)
        {
            if (item == null)
            {
                _logger.LogWarning("嘗試刪除一個無效的 DocumentItem (null)。");
                return;
            }

            // 在刪除資料庫紀錄前先記錄操作
            await LogActionAsync("Delete", item);

            // 取得寫入連線字串
            var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
            using var connection = new SqlConnection(connectionString);

            // 根據 Id 刪除資料庫紀錄的 SQL 語法
            const string deleteSql = "DELETE FROM A31_HWD_Portal_Document WHERE Id = @Id";
            var affectedRows = await connection.ExecuteAsync(deleteSql, new { Id = item.Id });

            // 判斷資料庫紀錄是否刪除成功
            if (affectedRows > 0)
            {
                _logger.LogInformation("已成功從資料庫刪除文件紀錄 (Id: {Id})。", item.Id);

                // 操作成功後，清除快取
                _cachedDocuments = null;

                // 如果資料庫紀錄刪除成功，且檔案路徑存在，則刪除實體檔案
                if (!string.IsNullOrEmpty(item.FileFullPath))
                {
                    try
                    {
                        if (File.Exists(item.FileFullPath))
                        {
                            File.Delete(item.FileFullPath);
                            _logger.LogInformation("已成功刪除實體檔案: {FilePath}", item.FileFullPath);
                        }
                        else
                        {
                            _logger.LogWarning("找不到要刪除的實體檔案: {FilePath}", item.FileFullPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "刪除實體檔案時發生錯誤: {FilePath}", item.FileFullPath);
                        // [註解] 即使檔案刪除失敗也不拋出例外，因為資料庫紀錄已刪除，這是主要的
                    }
                }
            }
            else
            {
                _logger.LogWarning("嘗試從資料庫刪除文件紀錄失敗，可能已被他人刪除 (Id: {Id})。", item.Id);
            }
        }
    }
}