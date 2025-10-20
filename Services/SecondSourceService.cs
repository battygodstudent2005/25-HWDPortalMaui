using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Dapper;
using Microsoft.Data.SqlClient;

namespace HWDPortalMaui.Services
{
    public class SecondSourceItem
    {
        public int Id { get; set; }
        public int? Year { get; set; }
        public string? Date { get; set; }
        public string CandidatePN { get; set; } = string.Empty;
        public string CandidateVender { get; set; } = string.Empty;
        public string ComponentStatus { get; set; } = string.Empty;
        public string MainSourceMPN { get; set; } = string.Empty;
        public string MainSourceCPN { get; set; } = string.Empty;

        // 補上 'Component Substitute Mode' 欄位
        public string? ComponentSubstituteMode { get; set; }

        public string? PowerDepartment { get; set; }
        public string? ComponentType { get; set; }
        public string? EVKitReport { get; set; }
        public string? TestMBOfMainSource { get; set; }
        public string? SecondSourceRealMBEAReport { get; set; }
        public string? PowerFMDMComments { get; set; }

        // 補上 'Application Area...' 相關欄位
        public string? ApplicationAreaAsc { get; set; }
        public string? ApplicationAreaTxt { get; set; }
    }

    /// <summary>
    /// 負責提供替代料 (Second Source) 資訊的服務
    /// </summary>
    public class SecondSourceService
    {
        private readonly ILogger<SecondSourceService> _logger;
        private readonly IConfiguration _configuration;

        private static List<SecondSourceItem>? _cachedItems;
        private static DateTime _lastCacheTime;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public SecondSourceService(ILogger<SecondSourceService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// 從資料庫或快取中取得替代料列表
        /// </summary>
        public async Task<List<SecondSourceItem>> GetSecondSourceItemsAsync(bool forceReload = false)
        {
            // 快取檢查邏輯 (保持不變)
            if (!forceReload && _cachedItems != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
            {
                _logger.LogInformation("從快取中讀取替代料 (Second Source) 列表。");
                return _cachedItems;
            }

            await _semaphore.WaitAsync();
            try
            {
                if (!forceReload && _cachedItems != null && DateTime.UtcNow - _lastCacheTime < _cacheDuration)
                {
                    return _cachedItems;
                }

                _logger.LogInformation("快取過期或被強制更新，正在從資料庫重新載入替代料 (Second Source) 列表...");

                var items = new List<SecondSourceItem>();
                try
                {
                    var connectionString = _configuration.GetConnectionString("ReadConnection_A31_HWD_PWR");
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("appsettings.json 中未設定 'ReadConnection_A31_HWD_PWR' 連線字串。");
                    }

                    using var connection = new SqlConnection(connectionString);

                    // SQL 查詢語法
                    // 已補上所有欄位，並使用 TRY_CONVERT 處理日期
                    const string sql = @"
                        SELECT 
                            Id, 
                            
                            -- Year 欄位嘗試從 nvarchar 字串的前 4 碼轉換
                            TRY_CAST(LEFT([Date.], 4) AS INT) AS Year,
                            
                            -- Date 欄位直接讀取 nvarchar (string)
                            [Date.] AS Date,       
                            
                            [Candidate PN] AS CandidatePN,
                            [Candidate Vender] AS CandidateVender,
                            [Component Status] AS ComponentStatus,
                            [Main Source MPN] AS MainSourceMPN,
                            [Main Source CPN] AS MainSourceCPN,

                            [Component Substitute Mode] AS ComponentSubstituteMode,
                            [Power Department] AS PowerDepartment,
                            [Component Type] AS ComponentType,
                            [EV-Kit Report      ] AS EVKitReport, 
                            [Test MB of Main Source_of Project] AS TestMBOfMainSource,
                            [The 2nd Source Real MB EA Report] AS SecondSourceRealMBEAReport,
                            [Power FM/DM commonts] AS PowerFMDMComments,
                            [Application Area if has Remark asc file InHere] AS ApplicationAreaAsc,
                            [Application Area if has Remark as TXT file InHere (VE only)] AS ApplicationAreaTxt

                        FROM 
                            A31_HWD_Portal_PWR_SecondSourceStatus
                        ORDER BY 
                            -- 由於 'YYYY_MM_DD' 格式，可以直接用 nvarchar 排序
                            [Date.] DESC;
                    ";

                    var result = await connection.QueryAsync<SecondSourceItem>(sql);
                    items = result.ToList();

                    // 更新快取
                    _cachedItems = items;
                    _lastCacheTime = DateTime.UtcNow;

                    _logger.LogInformation("成功從資料庫載入並快取了 {Count} 筆替代料資料。", items.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "從資料庫載入替代料 (Second Source) 時發生錯誤。");
                    _cachedItems = new List<SecondSourceItem>();
                    _lastCacheTime = DateTime.UtcNow;
                }

                return _cachedItems;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}