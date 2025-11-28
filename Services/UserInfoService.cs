namespace HWDPortalMaui.Services
{
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Configuration;
    using System.Data; // 記得引用

    // 這個類別放在您的主專案中
    public class UserInfoService
    {
        // 用於從組態檔中讀取設定
        private readonly IConfiguration _configuration;


        // 一個唯一的ID來識別這個物件實例
        public Guid InstanceId { get; }

        // 新增建構函式
        // 修改建構函式，注入 IConfiguration
        public UserInfoService(IConfiguration configuration)
        {
            // 將注入的 IConfiguration 物件賦值給我們的欄位
            _configuration = configuration;
            // 當這個類別的 new 物件被建立時，賦予它一個新的 Guid
            InstanceId = Guid.NewGuid();
        }

        // 這裡的屬性定義和 Client 端的版本保持一致
        public string? UserName { get; private set; }
        public string? UserDivision { get; private set; }
        public string? UserDepartment { get; private set; }
        public string? UserRole { get; private set; }
        public string? UserLocation { get; private set; }
        public bool IsUserValid { get; private set; } = false; // 用於標記使用者是否存在於資料庫中
        public bool IsInitialized { get; private set; } = false;
        public DateTime SQLdatatime { get; private set; } = DateTime.MinValue;

        // 用於儲存錯誤訊息 (例如資料庫連線失敗)
        public string? ErrorMessage { get; private set; }

        // 伺服器端的事件處理比較複雜，為了簡化，我們先專注於讓程式能啟動
        // 在伺服器端靜態渲染時，事件通知 (event) 不是主要問題

        // 初始化方法
        public async Task InitializeAsync(string userName)
        {
            if (IsInitialized) return;

            UserName = userName;
            // 重置錯誤訊息
            ErrorMessage = null;
            try
            {
                // 在伺服器端，我們直接呼叫資料庫存取方法
                DataTable membertable = await ReadSQLdataAsync("MEM_ALL");

                // 檢查是否成功讀取到資料表，ReadSQLdataAsync 失敗會回傳 null
                if (membertable == null)
                {
                    ErrorMessage = "無法連接至 SQL 資料庫或讀取 'MEM_ALL' 失敗。請檢查伺服器狀態或網路連線。";
                    IsUserValid = false;
                    return; // 提早結束，避免後續 NullReferenceException
                }

                DataRow? userRow = null;
                foreach (DataRow row in membertable.Rows)
                {
                    if (UserName.Equals(row[3].ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        userRow = row;
                        break;
                    }
                }
                if (userRow != null)
                {
                    UserDivision = userRow[1].ToString();
                    UserDepartment = userRow[2].ToString();
                    UserRole = userRow[4].ToString();
                    UserLocation = userRow[5].ToString()?.ToUpper();
                    if (UserDivision == "A31HWD") { UserDivision = "A31_HWD"; } // workaround
                    IsUserValid = true; // 找到使用者，標記為有效
                }
                else
                {
                    IsUserValid = false; // 找不到使用者，標記為無效
                }
            }
            catch (Exception ex)
            {
                // 將錯誤訊息記錄下來，以便前端顯示
                ErrorMessage = $"載入使用者資訊時發生未預期的例外: {ex.Message}";
                Console.WriteLine($"Error loading user info directly from DB: {ex.Message}");
                IsUserValid = false;
            }
            finally
            {
                IsInitialized = true;
            }
        }

        // 您真實的資料庫存取方法
        private async Task<DataTable> ReadSQLdataAsync(string readsheet)
        {
            // 連接字串
            string connetionString;
            SqlConnection cnn;

            // 替換成你的資料庫連接設定
            connetionString = _configuration.GetConnectionString("ReadConnection_A31_HWD_PWR");

            // 初始化連接物件
            cnn = new SqlConnection(connetionString);

            try
            {
                // 嘗試打開連接
                cnn.Open();

                // 獲取最後修改時間
                string query = @"
            SELECT MAX(last_user_update) AS last_modified_time
            FROM sys.dm_db_index_usage_stats
            WHERE database_id = DB_ID()
            AND object_id = OBJECT_ID(@tableName)";

                using (SqlCommand cmd = new SqlCommand(query, cnn))
                {
                    // 新增參數，將 @tableName 的值設定為 readsheet
                    cmd.Parameters.AddWithValue("@tableName", readsheet);

                    // 執行查詢並取得結果
                    object result = cmd.ExecuteScalar();

                    // 如果結果不為 null 且不是 DBNull.Value，處理結果
                    if (result != null && result != DBNull.Value)
                    {
                        // 將結果轉換為 DateTime 型別並賦值給 SQLdatatime
                        SQLdatatime = (DateTime)result;

                        // 處理最後修改時間，例如顯示於介面上
                        //SQLdatatime = lastModified.ToString("yyyy/MM/dd tt hh:mm:ss"); // 將日期時間格式化為所需的字串格式
                    }
                    else
                    {
                        // 處理未找到修改時間的情況
                    }
                }

                // 在這裡執行讀取資料的操作
                DataTable dataTable = ReadData(cnn, readsheet);
                return dataTable;
            }
            catch (Exception ex)
            {
                // 如果連接打不開，顯示錯誤訊息
                //MessageBox.Show("Can not open connection! " + ex.Message);
                return null; // 如果有錯誤，返回 null 或者一個適當的值
            }
            finally
            {
                // 無論如何，都要確保連接被關閉
                if (cnn.State == ConnectionState.Open)
                    cnn.Close();
            }
        }
        private DataTable ReadData(SqlConnection connection, string readsheet)
        {
            try
            {
                // 資料庫查詢
                string query = "SELECT * FROM " + readsheet;

                // 初始化命令物件
                SqlCommand cmd = new SqlCommand(query, connection);

                // 初始化資料適配器
                SqlDataAdapter adapter = new SqlDataAdapter(cmd);

                // 初始化資料表
                DataTable dataTable = new DataTable();

                // 使用資料適配器填充資料表
                adapter.Fill(dataTable);

                // 示範：
                //dataGridView1.DataSource = dataTable;

                return dataTable;
            }
            catch (Exception ex)
            {
                // 如果發生錯誤，顯示錯誤訊息
                //MessageBox.Show("Error reading data: " + ex.Message);
                return null; // 如果有錯誤，返回 null 或者一個適當的值
            }
        }
    }
}

