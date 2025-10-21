using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
namespace HWDPortalMaui.Services
{
    /// <summary>
    /// EmailService 類別用來發送電子郵件，通過 HTTP POST 請求將郵件訊息發送到指定的 SMTP 伺服器。
    /// </summary>
    public class EmailService
    {
        // 定義版本號
        public const string Version = "1.0.0";
        // 定義版本發布日期
        public const string ReleaseDate = "2024-10-30";
        // 定義郵件發送 API URL的常量
        private const string url = "http://tpea31hwddb01:5000/smtp";

        // [新增] 透過 IHttpClientFactory 和 ILogger 進行依賴注入
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IHttpClientFactory httpClientFactory, ILogger<EmailService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// 發送電子郵件的非同步方法，支援寄件副本（CC）。
        /// </summary>
        /// <param name="apiKey">API 金鑰。</param>
        /// <param name="fromAddress">發件者的電子郵件地址。</param>
        /// <param name="toAddresses">收件者的電子郵件地址（可以是多個地址，用分號分隔）。</param>
        /// <param name="subject">郵件的主題。</param>
        /// <param name="content">郵件的內容。</param>
        /// <param name="ccAddresses">寄件副本地址（可選），多個地址用分號分隔。</param>
        /// <returns>如果郵件成功發送，回傳 true；否則回傳 false。</returns>
        public async Task<bool> SendEmailAsync(string apiKey, string fromAddress, string toAddresses, string subject, string content, string ccAddresses = null)
        {
            // 構建要發送的郵件內容，作為匿名物件
            var mailRequest = new
            {
                api_key = apiKey,
                from_addr = fromAddress,
                to_addr = toAddresses,
                cc_addr = ccAddresses,
                subject = subject,
                content = content
            };

            // [修改] 從 IHttpClientFactory 建立 HttpClient
            var client = _httpClientFactory.CreateClient();

            try
            {
                // [修改] 使用 System.Text.Json.JsonSerializer 進行序列化
                var jsonBody = JsonSerializer.Serialize(mailRequest);
                var contentBody = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                // 發送 HTTP POST 請求到指定的 URL
                HttpResponseMessage response = await client.PostAsync(url, contentBody);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Email sent successfully to {To}", toAddresses);
                    return true;
                }
                else
                {
                    // [修改] 記錄更詳細的錯誤
                    string errorResponse = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Failed to send email. Status: {StatusCode}, Response: {Response}", response.StatusCode, errorResponse);
                    return false;
                }
            }
            catch (Exception ex)
            {
                // [修改] 使用 ILogger 記錄例外
                _logger.LogError(ex, "Error sending email: {ErrorMessage}", ex.Message);
                return false;
            }
        }
    }
}