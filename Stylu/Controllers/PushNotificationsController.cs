using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Stylu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            HttpClient httpClient, 
            IConfiguration config,
            ILogger<NotificationController> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Save a notification to Supabase using Service Role key (server-side)
        /// This endpoint works even when user is logged out
        /// </summary>
        [HttpPost("save")]
        public async Task<IActionResult> SaveNotification([FromBody] SaveNotificationRequest request)
        {
            try
            {
                _logger.LogInformation("Saving notification for user: {UserId}", request.UserId);

                // Validate request
                if (string.IsNullOrEmpty(request.UserId))
                    return BadRequest(new { error = "userId is required" });

                if (string.IsNullOrEmpty(request.Title))
                    return BadRequest(new { error = "title is required" });

                if (string.IsNullOrEmpty(request.Message))
                    return BadRequest(new { error = "message is required" });

                // Get Supabase configuration
                var supabaseUrl = _config["Supabase:Url"];
                var serviceRoleKey = _config["Supabase:ServiceRoleKey"]; // ⚠️ IMPORTANT: Use Service Role, not Anon key

                if (string.IsNullOrEmpty(serviceRoleKey))
                {
                    _logger.LogError("Supabase Service Role Key not configured");
                    return StatusCode(500, new { error = "Server configuration error" });
                }

                // Use provided timestamp or current UTC time
                var timestamp = string.IsNullOrEmpty(request.Timestamp)
                    ? DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss")
                    : request.Timestamp;

                // Prepare notification data
                var notificationData = new
                {
                    user_id = request.UserId,
                    title = request.Title,
                    message = request.Message,
                    type = request.Type ?? "general",
                    scheduled_at = timestamp,
                    sent_at = timestamp,
                    status = request.Status ?? "sent",
                    data = request.Data // Additional JSONB data
                };

                _logger.LogDebug("Notification payload: {Payload}", JsonSerializer.Serialize(notificationData));

                // Create request to Supabase
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, 
                    $"{supabaseUrl}/rest/v1/notifications")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(notificationData),
                        Encoding.UTF8,
                        "application/json"
                    )
                };

                // ⚠️ CRITICAL: Use Service Role key for server-side operations
                httpRequest.Headers.Add("apikey", serviceRoleKey);
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);
                httpRequest.Headers.Add("Prefer", "return=representation");

                // Send to Supabase
                var response = await _httpClient.SendAsync(httpRequest);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Supabase error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                    return StatusCode((int)response.StatusCode, new 
                    { 
                        error = "Failed to save notification",
                        details = responseBody 
                    });
                }

                _logger.LogInformation("✅ Notification saved successfully for user: {UserId}", request.UserId);

                // Parse and return the saved notification
                var savedNotification = JsonSerializer.Deserialize<JsonElement>(responseBody);

                return Ok(new
                {
                    success = true,
                    message = "Notification saved successfully",
                    notification = savedNotification
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving notification");
                return StatusCode(500, new 
                { 
                    error = "Internal server error", 
                    details = ex.Message 
                });
            }
        }

        /// <summary>
        /// Batch save multiple notifications (useful for syncing queued notifications)
        /// </summary>
        [HttpPost("save-batch")]
        public async Task<IActionResult> SaveNotificationBatch([FromBody] BatchSaveRequest batchRequest)
        {
            try
            {
                _logger.LogInformation("Batch saving {Count} notifications", batchRequest.Notifications.Count);

                if (batchRequest.Notifications == null || batchRequest.Notifications.Count == 0)
                    return BadRequest(new { error = "notifications array is required and cannot be empty" });

                var supabaseUrl = _config["Supabase:Url"];
                var serviceRoleKey = _config["Supabase:ServiceRoleKey"];

                if (string.IsNullOrEmpty(serviceRoleKey))
                {
                    _logger.LogError("Supabase Service Role Key not configured");
                    return StatusCode(500, new { error = "Server configuration error" });
                }

                var successCount = 0;
                var failureCount = 0;
                var errors = new List<string>();

                foreach (var notif in batchRequest.Notifications)
                {
                    try
                    {
                        var timestamp = string.IsNullOrEmpty(notif.Timestamp)
                            ? DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss")
                            : notif.Timestamp;

                        var notificationData = new
                        {
                            user_id = notif.UserId,
                            title = notif.Title,
                            message = notif.Message,
                            type = notif.Type ?? "general",
                            scheduled_at = timestamp,
                            sent_at = timestamp,
                            status = notif.Status ?? "sent",
                            data = notif.Data
                        };

                        var httpRequest = new HttpRequestMessage(HttpMethod.Post,
                            $"{supabaseUrl}/rest/v1/notifications")
                        {
                            Content = new StringContent(
                                JsonSerializer.Serialize(notificationData),
                                Encoding.UTF8,
                                "application/json"
                            )
                        };

                        httpRequest.Headers.Add("apikey", serviceRoleKey);
                        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceRoleKey);

                        var response = await _httpClient.SendAsync(httpRequest);

                        if (response.IsSuccessStatusCode)
                        {
                            successCount++;
                        }
                        else
                        {
                            failureCount++;
                            var errorBody = await response.Content.ReadAsStringAsync();
                            errors.Add($"Failed to save notification '{notif.Title}': {errorBody}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        errors.Add($"Exception for notification '{notif.Title}': {ex.Message}");
                    }
                }

                _logger.LogInformation("Batch complete: {Success} succeeded, {Failed} failed", 
                    successCount, failureCount);

                return Ok(new
                {
                    success = failureCount == 0,
                    successCount,
                    failureCount,
                    errors = errors.Any() ? errors : null
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch save");
                return StatusCode(500, new 
                { 
                    error = "Internal server error", 
                    details = ex.Message 
                });
            }
        }
    }

    // Request models
    public class SaveNotificationRequest
    {
        public string UserId { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public string? Type { get; set; }
        public string? Timestamp { get; set; }
        public string? Status { get; set; }
        public Dictionary<string, object>? Data { get; set; }
    }

    public class BatchSaveRequest
    {
        public List<SaveNotificationRequest> Notifications { get; set; }
    }
}
