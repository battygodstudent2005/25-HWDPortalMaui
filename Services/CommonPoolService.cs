// 檔案路徑: Services/CommonPoolService.cs

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Dapper;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Linq;
using System.Data; // [新增] 為了使用 DataTable

namespace HWDPortalMaui.Services
{
    // [移除] SecondSourceItem 模型相關備註 (如果不再使用)

    /// <summary>
    /// 負責提供 Common Pool (替代料) 資訊的服務
    /// </summary>
    public class CommonPoolService
    {
        private readonly ILogger<CommonPoolService> _logger;
        private readonly IConfiguration _configuration;

        // [修改] 快取機制調整 - Value 改為 DataTable
        // Key: ComponentType, Value: 該類型的資料表
        private static Dictionary<string, DataTable> _cachedItems = new Dictionary<string, DataTable>();
        private static DateTime _lastCacheTime;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public CommonPoolService(ILogger<CommonPoolService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// 從資料庫或快取中取得特定 ComponentType 的替代料列表 (DataTable)
        /// </summary>
        /// <param name="componentType">要查詢的元件類型</param>
        /// <param name="forceReload">是否強制重新載入 (清除所有快取)</param>
        // [修改] 回傳類型改為 Task<DataTable>
        public async Task<DataTable> GetCommonPoolItemsAsync(string componentType, bool forceReload = false)
        {
            // 1. 檢查快取是否過期
            bool cacheExpired = DateTime.UtcNow - _lastCacheTime >= _cacheDuration;

            if (forceReload || cacheExpired)
            {
                if (forceReload) _logger.LogInformation("要求強制重新載入，將清空 Common Pool 快取。");
                if (cacheExpired) _logger.LogInformation("Common Pool 快取過期，將清空。");

                _cachedItems.Clear();
            }
            // 2. 檢查特定類型的快取是否存在且有效
            else if (_cachedItems.ContainsKey(componentType))
            {
                _logger.LogInformation("從快取中讀取 ComponentType: {ComponentType} 的替代料列表。", componentType);
                return _cachedItems[componentType]; // [修改] 回傳特定類型的快取 (DataTable)
            }

            // 進入 Semaphore
            await _semaphore.WaitAsync();
            try
            {
                // 再次檢查快取 (Double-check locking)
                if (_cachedItems.ContainsKey(componentType))
                {
                    // [修改] 檢查是否在等待時，其他執行緒已經載入 *此 Type*
                    return _cachedItems[componentType];
                }

                // [修改] 如果快取中沒有這個 Type，我們需要從資料庫載入 *該 Type*
                _logger.LogInformation("正在從資料庫載入 ComponentType: {ComponentType} 的替代料列表...", componentType);

                try
                {
                    var connectionString = _configuration.GetConnectionString("ReadConnection_A31_HWD_PWR");
                    if (string.IsNullOrEmpty(connectionString))
                    {
                        throw new InvalidOperationException("appsettings.json 中未設定 'ReadConnection_A31_HWD_PWR' 連線字串。");
                    }

                    using var connection = new SqlConnection(connectionString);
                    await connection.OpenAsync(); // [新增] 使用 Reader 前需要 Open

                    // (Regex 檢查保持不變)
                    if (string.IsNullOrEmpty(componentType) ||
                        !System.Text.RegularExpressions.Regex.IsMatch(componentType, @"^[a-zA-Z0-9_]+$"))
                    {
                        throw new ArgumentException("無效的 ComponentType 格式。", nameof(componentType));
                    }

                    string tableName = $"SecondSourceList_{componentType}";
                    string sql = $"SELECT * FROM {tableName} ORDER BY [ID] DESC;";

                    // [修改] 改為使用 ExecuteReaderAsync 和 DataTable.Load

                    // [移除] var allItems = (await connection.QueryAsync<SecondSourceItem>(sql)).ToList();

                    using var command = connection.CreateCommand(); // [新增]
                    command.CommandText = sql; // [新增]
                    using var reader = await command.ExecuteReaderAsync(); // [新增]

                    var dataTable = new DataTable(); // [新增]
                    dataTable.Load(reader); // [新增] 讀取資料結構和內容

                    // [修改] 將讀取到的 DataTable 存入快取
                    _cachedItems[componentType] = dataTable;

                    // [移除] GroupBy 相關邏輯 (不再需要)
                    // var groupedData = allItems.GroupBy(item => item.ComponentType); ...

                    if (_cachedItems.Count == 1) // [修改] 如果這是第一個載入的快取
                    {
                        _lastCacheTime = DateTime.UtcNow; // 重置快取時間
                    }

                    _logger.LogInformation("成功從資料庫載入並快取了 {ComponentType} 的 {Count} 筆替代料資料。", componentType, dataTable.Rows.Count);

                    return dataTable; // [修改] 回傳新載入的 DataTable
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "從資料庫載入 {ComponentType} 替代料時發生錯誤。", componentType);
                    _lastCacheTime = DateTime.UtcNow;

                    // [修改] 發生錯誤時，回傳一個空的 DataTable
                    return new DataTable();
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}