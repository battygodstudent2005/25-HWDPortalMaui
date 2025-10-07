using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging; // ILogger<> 所需的 using

namespace HWDPortalMaui.Services
{
    public class DashboardService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardService> _logger;

        // 注入 IConfiguration 和 ILogger
        public DashboardService(IConfiguration configuration, ILogger<DashboardService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // 從 appsettings 取得連線字串的方法
        private string GetConnectionString(string key)
        {
            return _configuration.GetConnectionString(key);
        }

        // [修改] 將 ReadSQLdata 方法移至服務中
        public async Task<(DataTable data, DateTime queryTime)> ReadSQLdataAsync(string readsheet)
        {
            var connetionString = GetConnectionString("ReadConnection"); // [修改] 從組態檔取得連線字串
            if (string.IsNullOrEmpty(connetionString))
            {
                _logger.LogError("找不到 'ReadConnection' 連線字串。");
                return (new DataTable(), DateTime.MinValue);
            }

            DataTable dataTable = new DataTable();
            DateTime sqlQueryTime = DateTime.MinValue;

            using (SqlConnection cnn = new SqlConnection(connetionString))
            {
                try
                {
                    await cnn.OpenAsync();

                    string timeQuery = @"SELECT MAX(last_user_update) AS last_modified_time FROM sys.dm_db_index_usage_stats WHERE database_id = DB_ID() AND object_id = OBJECT_ID(@tableName)";
                    using (SqlCommand timeCmd = new SqlCommand(timeQuery, cnn))
                    {
                        timeCmd.Parameters.AddWithValue("@tableName", readsheet);
                        var result = await timeCmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                        {
                            sqlQueryTime = (DateTime)result;
                        }
                        else
                        {
                            sqlQueryTime = DateTime.Now;
                        }
                    }

                    string query = $"SELECT * FROM {readsheet}";
                    using (SqlDataAdapter adapter = new SqlDataAdapter(query, cnn))
                    {
                        adapter.Fill(dataTable);
                    }
                }
                catch (SqlException ex)
                {
                    _logger.LogError(ex, $"讀取 SQL 資料表時發生錯誤: {readsheet}");
                    return (new DataTable(), DateTime.MinValue);
                }
            }
            return (dataTable, sqlQueryTime);
        }

        // 將 UpdateToServer 的邏輯移至服務中
        public async Task<int> UpdateProjectDashboardRecordsAsync(DataTable modifiedData)
        {
            if (modifiedData == null || modifiedData.Rows.Count == 0) return 0;

            string connectionString = GetConnectionString("WriteConnection"); // [修改] 使用具備寫入權限的連線字串
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("找不到 'WriteConnection' 連線字串。");
                throw new InvalidOperationException("無法連接資料庫，缺少連線字串。");
            }

            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();

                var mergeCommandText = @"... (將 UpdateToServer 中的 SQL MERGE 語法貼過來) ...";

                int recordsUpdated = 0;
                foreach (DataRow row in modifiedData.Rows)
                {
                    using (SqlCommand cmd = new SqlCommand(mergeCommandText, conn))
                    {
                        // [修改] 新增參數
                        cmd.Parameters.AddWithValue("@Division", row["Division"]);
                        cmd.Parameters.AddWithValue("@Department", row["Department"]);
                        cmd.Parameters.AddWithValue("@ProjectCode", row["ProjectCode"]);
                        cmd.Parameters.AddWithValue("@ProjectYear", row["ProjectYear"]);
                        cmd.Parameters.AddWithValue("@Stage", row["Stage"]);
                        cmd.Parameters.AddWithValue("@Item", row["Item"]);
                        cmd.Parameters.AddWithValue("@UserName", row["UserName"]);
                        cmd.Parameters.AddWithValue("@UserAction", row["UserAction"]);
                        cmd.Parameters.AddWithValue("@UpdateTime", row["UpdateTime"]);

                        await cmd.ExecuteNonQueryAsync();
                        recordsUpdated++;
                    }
                }
                return recordsUpdated;
            }
        }
    }
}