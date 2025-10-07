// 引用 Microsoft.AspNetCore.Components.Forms 來處理 IBrowserFile
using Microsoft.AspNetCore.Components.Forms;
// 引用 Dapper 和 SQL Server 客戶端
using Dapper;
using Microsoft.Data.SqlClient;
using System.IO;
using Microsoft.Extensions.Configuration; // [新增] 引用 Configuration 命名空間
using Microsoft.Extensions.Logging;       // [新增] 引用 Logging 命名空間
using Microsoft.Maui.Storage;             // [新增] 引用 MAUI 檔案系統 API

namespace HWDPortalMaui.Services
{
    // 用於表示公告資料的公開類別，以便在專案中其他地方使用
    public class BulletinItem
    {
        public int Id { get; set; } // 公告的唯一識別碼
        public string? Department { get; set; } // 部門
        public string? Document { get; set; } // 文件標題 (對應之前的 Title)
        public string? FileFullPath { get; set; } // 檔案完整路徑
        public DateTime? UploadTime { get; set; } // 上傳時間 (對應之前的 LastWriteTime)
    }

    public class BulletinService
    {
        private readonly ILogger<BulletinService> _logger;
        // 用於從組態檔中讀取設定
        private readonly IConfiguration _configuration;

        // 用於取得使用者資訊的服務
        private readonly UserInfoService _userInfoService; 

        // 用於快取公告列表
        private List<BulletinItem>? _bulletinsCache;
        // 記錄上次載入快取的時間
        private DateTime _lastLoadTime;
        // 設定快取的有效時間，例如 5 分鐘
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        // 使用 SemaphoreSlim 來防止多個執行緒同時更新快取，確保執行緒安全
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        // 修改建構式，注入 IConfiguration 和 IWebHostEnvironment
        public BulletinService(ILogger<BulletinService> logger, IConfiguration configuration, UserInfoService userInfoService)
        {
            _logger = logger;
            _configuration = configuration;
            // [移除] _env = env;
            _userInfoService = userInfoService;
        }

        // 修改 GetBulletinsAsync 方法，增加 forceReload 參數以強制更新快取
        public async Task<List<BulletinItem>?> GetBulletinsAsync(bool forceReload = false)
        {
            // 加上 !forceReload 條件
            if (!forceReload && _bulletinsCache != null && _lastLoadTime.Add(_cacheDuration) > DateTime.UtcNow)
            {
                _logger.LogInformation("從快取中返回公告資料。");
                return _bulletinsCache;
            }

            // ... (信號量邏輯不變)
            await _semaphore.WaitAsync();
            try
            {
                // 加上 !forceReload 條件
                if (!forceReload && _bulletinsCache != null && _lastLoadTime.Add(_cacheDuration) > DateTime.UtcNow)
                {
                    _logger.LogInformation("從快取中返回公告資料 (在等待後)。");
                    return _bulletinsCache;
                }

                _logger.LogInformation("快取過期、不存在或被強制更新，正在從資料庫重新載入...");
                // 呼叫從資料庫載入的方法
                await LoadBulletinsFromDatabaseAsync();
                return _bulletinsCache;
            }
            finally
            {
                // 釋放信號量
                _semaphore.Release();
            }
        }

        // [新增] 紀錄檔案操作 (上傳/刪除) 的私有方法
        private async Task LogActionAsync(string actionType, BulletinItem item, string? physicalFileName)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
                using var connection = new SqlConnection(connectionString);
                const string sql = @"
            INSERT INTO A31_HWD_Portal_Bulletin_FileAction_Record 
            (Division, Department, UserName, Document, ActionType, FileName, ActionTime)
            VALUES 
            (@Division, @Department, @UserName, @Document, @ActionType, @FileName, @ActionTime)";

                await connection.ExecuteAsync(sql, new
                {
                    Division = _userInfoService.UserDivision, // [新增] 從 UserInfoService 取得 Division
                    item.Department, // [新增] 從傳入的 item 取得部門
                    _userInfoService.UserName, // [新增] 從 UserInfoService 取得使用者名稱
                    item.Document, // [新增] 原始文件名稱
                    ActionType = actionType, // [新增] 操作類型 ('Upload' 或 'Delete')
                    FileName = physicalFileName, // [新增] 伺服器上的實際檔案路徑
                    ActionTime = DateTime.Now // [新增] 當前時間
                });
                _logger.LogInformation($"已成功紀錄操作: {actionType}, 使用者: {_userInfoService.UserName}, 檔案: {item.Document}");
            }
            catch (Exception ex)
            {
                // [新增] 紀錄日誌失敗不應影響主要操作，所以只記錄錯誤訊息
                _logger.LogError(ex, "紀錄檔案操作時發生錯誤。");
            }
        }
        // 上傳新公告的方法
        public async Task UploadBulletinAsync(string department, IBrowserFile file)
        {
            // 從 appsettings.json 讀取儲存路徑的設定
            var storedFilesPath = _configuration.GetValue<string>("StoredFilesPath");
            if (string.IsNullOrEmpty(storedFilesPath))
            {
                throw new InvalidOperationException("appsettings.json 中未設定 StoredFilesPath。");
            }

            // [修改] 使用 MAUI 的 FileSystem.AppDataDirectory 來取得平台通用的應用程式資料目錄
            var uploadPath = Path.Combine(FileSystem.AppDataDirectory, storedFilesPath);
            // 如果目錄不存在，就建立它
            Directory.CreateDirectory(uploadPath);

            // 為了避免檔名衝突和路徑遍歷攻擊，建立一個安全的新檔名
            var safeFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.Name)}";
            var fullPath = Path.Combine(uploadPath, safeFileName);

            _logger.LogInformation($"準備上傳檔案至: {fullPath}");

            // 將上傳的檔案流寫入到目標路徑
            await using (var fs = new FileStream(fullPath, FileMode.Create))
            {
                // 限制最大檔案大小為 100MB
                await file.OpenReadStream(100 * 1024 * 1024).CopyToAsync(fs);
            }

            try
            {
                var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
                using var connection = new SqlConnection(connectionString);
                const string sql = @"
        INSERT INTO A31_HWD_Portal_Bulletin (Department, Document, FileFullPath, UploadTime)
        VALUES (@Department, @Document, @FileFullPath, @UploadTime)";

                await connection.ExecuteAsync(sql, new
                {
                    Department = department,
                    Document = file.Name, // 儲存原始檔名
                    FileFullPath = fullPath, // 儲存包含安全檔名的完整實體路徑
                    UploadTime = DateTime.Now
                });

                _logger.LogInformation($"檔案 {file.Name} 的資料庫紀錄新增成功。");

                // [新增] 建立一個 BulletinItem 物件來傳遞給紀錄方法
                var uploadedItem = new BulletinItem { Department = department, Document = file.Name };
                // [新增] 呼叫紀錄方法
                await LogActionAsync("Upload", uploadedItem, fullPath);

                // 操作成功後，清除快取，以便下次取得最新資料
                _bulletinsCache = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "新增公告到資料庫時發生錯誤。");
                // 如果資料庫寫入失敗，最好將剛才儲存的檔案刪除，以保持資料一致性
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    _logger.LogInformation($"已刪除因資料庫錯誤而無法紀錄的檔案: {fullPath}");
                }
                // 拋出例外，讓前端知道操作失敗
                throw;
            }
        }


        // 刪除公告的方法
        public async Task DeleteBulletinAsync(int id)
        {
            var connectionString = _configuration.GetConnectionString("WriteConnection_A31_HWD");
            using var connection = new SqlConnection(connectionString);

            // 步驟 1: 從資料庫找出要刪除的完整公告資訊，以便紀錄
            const string findSql = "SELECT Id, Department, Document, FileFullPath FROM A31_HWD_Portal_Bulletin WHERE Id = @Id";
            var bulletinToDelete = await connection.QuerySingleOrDefaultAsync<BulletinItem>(findSql, new { Id = id });


            if (bulletinToDelete == null)
            {
                _logger.LogWarning($"嘗試刪除一個不存在的公告紀錄 (Id: {id})。");
                return; // [修改] 如果紀錄不存在，直接返回，不執行後續操作
            }
            // [新增] 步驟 2: 在刪除前先記錄操作
            await LogActionAsync("Delete", bulletinToDelete, bulletinToDelete.FileFullPath);


            // [修改] 步驟 3: 刪除資料庫紀錄
            const string deleteSql = "DELETE FROM A31_HWD_Portal_Bulletin WHERE Id = @Id";
            var affectedRows = await connection.ExecuteAsync(deleteSql, new { Id = id });

            if (affectedRows > 0)
            {
                _logger.LogInformation($"已成功從資料庫刪除公告 (Id: {id})。");

                // 操作成功後，清除快取
                _bulletinsCache = null;

                // [修改] 步驟 4: 如果資料庫紀錄刪除成功，且檔案路徑存在，則刪除實體檔案
                if (!string.IsNullOrEmpty(bulletinToDelete.FileFullPath))
                {
                    try
                    {
                        if (File.Exists(bulletinToDelete.FileFullPath))
                        {
                            File.Delete(bulletinToDelete.FileFullPath);
                            _logger.LogInformation($"已成功刪除實體檔案: {bulletinToDelete.FileFullPath}");
                        }
                        else
                        {
                            _logger.LogWarning($"找不到要刪除的實體檔案: {bulletinToDelete.FileFullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"刪除實體檔案時發生錯誤: {bulletinToDelete.FileFullPath}");
                        // 即使檔案刪除失敗，我們也不拋出例外，因為資料庫紀錄已經刪除
                    }
                }
            }
        }
        // 此方法已完全重寫，從檔案系統邏輯改為資料庫查詢邏輯

        // [新增] 根據 Id 取得單一筆公告的資訊
        public async Task<BulletinItem?> GetBulletinByIdAsync(int id)
        {
            try
            {
                var connectionString = _configuration.GetConnectionString("ReadConnection_A31_HWD");
                using var connection = new SqlConnection(connectionString);
                const string sql = "SELECT Id, Department, Document, FileFullPath, UploadTime FROM A31_HWD_Portal_Bulletin WHERE Id = @Id";

                // 使用 Dapper 的 QuerySingleOrDefaultAsync 來查詢單一筆資料
                var bulletin = await connection.QuerySingleOrDefaultAsync<BulletinItem>(sql, new { Id = id });

                if (bulletin == null)
                {
                    _logger.LogWarning($"在資料庫中找不到 Id 為 {id} 的公告。");
                }

                return bulletin;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"根據 Id ({id}) 查詢公告時發生錯誤。");
                return null; // 發生錯誤時回傳 null
            }
        }
        private async Task LoadBulletinsFromDatabaseAsync()
        {
            try
            {
                // 從 appsettings.json 取得我們設定的連線字串
                var connectionString = _configuration.GetConnectionString("ReadConnection_A31_HWD");

                // 建立並開啟 SQL 連線
                using var connection = new SqlConnection(connectionString);

                // SQL 查詢語法，以對應新的資料表 (A31_HWD_Portal_Bulletin) 和欄位
                const string sql = "SELECT Id, Department, Document, FileFullPath, UploadTime FROM A31_HWD_Portal_Bulletin ORDER BY UploadTime DESC";

                // 使用 Dapper 執行非同步查詢，並將結果對應到 List<BulletinItem>
                var bulletinList = (await connection.QueryAsync<BulletinItem>(sql)).ToList();
                _logger.LogInformation($"從資料庫成功載入 {bulletinList.Count} 則公告。");

                // 更新快取和時間戳記
                _bulletinsCache = bulletinList;
                _lastLoadTime = DateTime.UtcNow;
                _logger.LogInformation("公告資料已成功載入並更新快取。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "從資料庫載入公告時發生錯誤。");
                // 即使發生錯誤，也更新快取為空列表，避免一直重試失敗的操作
                _bulletinsCache = new List<BulletinItem>();
                _lastLoadTime = DateTime.UtcNow;
            }
        }
    }
}