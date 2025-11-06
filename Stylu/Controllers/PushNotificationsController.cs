using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Stylu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PushNotificationController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly FirebaseMessaging _messaging;

        public PushNotificationController(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;

            // Initialize Firebase Admin SDK (do this once in Program.cs or here)
            if (FirebaseApp.DefaultInstance == null)
            {
                var credential = GoogleCredential.FromJson(_config["Firebase:ServiceAccountJson"]);
                FirebaseApp.Create(new AppOptions()
                {
                    Credential = credential
                });
            }
            _messaging = FirebaseMessaging.DefaultInstance;
        }

        // POST: api/PushNotification/register
        [HttpPost("register")]
        [Authorize]
        public async Task<IActionResult> RegisterToken([FromBody] JsonElement requestBody)
        {
            try
            {
                var userToken = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(userToken))
                    return Unauthorized(new { error = "Missing token" });

                var token = userToken.Replace("Bearer ", "");
                var userId = ExtractUserIdFromToken(token);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Invalid token" });

                if (!requestBody.TryGetProperty("fcmToken", out var fcmTokenElement))
                    return BadRequest(new { error = "fcmToken is required" });

                var fcmToken = fcmTokenElement.GetString();
                var platform = requestBody.TryGetProperty("platform", out var platformElement)
                    ? platformElement.GetString() : "android";

                var supabaseUrl = _config["Supabase:Url"];
                var supabaseKey = _config["Supabase:AnonKey"];

                // Check if token already exists
                var checkRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"{supabaseUrl}/rest/v1/device_tokens?fcm_token=eq.{fcmToken}&select=*");
                checkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                checkRequest.Headers.Add("apikey", supabaseKey);

                var checkResponse = await _httpClient.SendAsync(checkRequest);
                var checkBody = await checkResponse.Content.ReadAsStringAsync();
                var existingTokens = JsonDocument.Parse(checkBody).RootElement;

                HttpRequestMessage request;
                if (existingTokens.GetArrayLength() > 0)
                {
                    // Update existing token
                    var updateData = new
                    {
                        user_id = userId,
                        is_active = true,
                        platform = platform,
                        updated_at = DateTime.UtcNow.ToString("o")
                    };

                    request = new HttpRequestMessage(HttpMethod.Patch,
                        $"{supabaseUrl}/rest/v1/device_tokens?fcm_token=eq.{fcmToken}")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(updateData),
                            Encoding.UTF8,
                            "application/json"
                        )
                    };
                }
                else
                {
                    // Insert new token
                    var insertData = new
                    {
                        user_id = userId,
                        fcm_token = fcmToken,
                        platform = platform,
                        is_active = true
                    };

                    request = new HttpRequestMessage(HttpMethod.Post,
                        $"{supabaseUrl}/rest/v1/device_tokens")
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(insertData),
                            Encoding.UTF8,
                            "application/json"
                        )
                    };
                    request.Headers.Add("Prefer", "return=representation");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("apikey", supabaseKey);

                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new { error = "Failed to register token" });

                return Ok(new
                {
                    success = true,
                    message = "Token registered successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        // POST: api/PushNotification/unregister
        [HttpPost("unregister")]
        [Authorize]
        public async Task<IActionResult> UnregisterToken([FromBody] JsonElement requestBody)
        {
            try
            {
                var userToken = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(userToken))
                    return Unauthorized(new { error = "Missing token" });

                var token = userToken.Replace("Bearer ", "");
                var userId = ExtractUserIdFromToken(token);
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized(new { error = "Invalid token" });

                var supabaseUrl = _config["Supabase:Url"];
                var supabaseKey = _config["Supabase:AnonKey"];

                var updateData = new { is_active = false };
                var request = new HttpRequestMessage(HttpMethod.Patch,
                    $"{supabaseUrl}/rest/v1/device_tokens?user_id=eq.{userId}")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(updateData),
                        Encoding.UTF8,
                        "application/json"
                    )
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("apikey", supabaseKey);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, new { error = "Failed to unregister token" });

                return Ok(new
                {
                    success = true,
                    message = "Token unregistered successfully"
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        // POST: api/PushNotification/send
        [HttpPost("send")]
        [Authorize]
        public async Task<IActionResult> SendNotification([FromBody] JsonElement requestBody)
        {
            try
            {
                var userToken = Request.Headers["Authorization"].ToString();
                if (string.IsNullOrEmpty(userToken))
                    return Unauthorized(new { error = "Missing token" });

                var token = userToken.Replace("Bearer ", "");
                var supabaseUrl = _config["Supabase:Url"];
                var supabaseKey = _config["Supabase:AnonKey"];

                if (!requestBody.TryGetProperty("title", out var titleElement) ||
                    !requestBody.TryGetProperty("body", out var bodyElement))
                    return BadRequest(new { error = "Title and body are required" });

                var title = titleElement.GetString();
                var body = bodyElement.GetString();

                // Get target user tokens
                string? targetUserId = null;
                if (requestBody.TryGetProperty("userId", out var userIdElement))
                    targetUserId = userIdElement.GetString();

                var tokensRequest = new HttpRequestMessage(HttpMethod.Get,
                    targetUserId != null
                        ? $"{supabaseUrl}/rest/v1/device_tokens?user_id=eq.{targetUserId}&is_active=eq.true&select=fcm_token"
                        : $"{supabaseUrl}/rest/v1/device_tokens?is_active=eq.true&select=fcm_token");

                tokensRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                tokensRequest.Headers.Add("apikey", supabaseKey);

                var tokensResponse = await _httpClient.SendAsync(tokensRequest);
                var tokensBody = await tokensResponse.Content.ReadAsStringAsync();
                var tokensData = JsonDocument.Parse(tokensBody).RootElement;

                if (tokensData.GetArrayLength() == 0)
                    return NotFound(new { error = "No active tokens found" });

                // Extract data payload if provided
                var dataPayload = new Dictionary<string, string>();
                if (requestBody.TryGetProperty("data", out var dataElement))
                {
                    foreach (var prop in dataElement.EnumerateObject())
                    {
                        dataPayload[prop.Name] = prop.Value.ToString();
                    }
                }

                // Send notifications
                var successCount = 0;
                var failureCount = 0;

                foreach (var tokenRecord in tokensData.EnumerateArray())
                {
                    var fcmToken = tokenRecord.GetProperty("fcm_token").GetString();

                    try
                    {
                        var message = new Message()
                        {
                            Token = fcmToken,
                            Notification = new Notification()
                            {
                                Title = title,
                                Body = body
                            },
                            Data = dataPayload,
                            Android = new AndroidConfig()
                            {
                                Priority = Priority.High,
                                Notification = new AndroidNotification()
                                {
                                    ChannelId = "stylu_channel",
                                    Sound = "default",
                                    Priority = NotificationPriority.MAX
                                }
                            }
                        };

                        var response = await _messaging.SendAsync(message);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failureCount++;

                        // Mark invalid tokens as inactive
                        if (ex.Message.Contains("registration-token-not-registered") ||
                            ex.Message.Contains("invalid-registration-token"))
                        {
                            await MarkTokenInactive(fcmToken, token, supabaseUrl, supabaseKey);
                        }
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = "Notifications sent",
                    successCount = successCount,
                    failureCount = failureCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to send notification", details = ex.Message });
            }
        }

        // POST: api/PushNotification/send-to-topic
        [HttpPost("send-to-topic")]
        [Authorize]
        public async Task<IActionResult> SendToTopic([FromBody] JsonElement requestBody)
        {
            try
            {
                if (!requestBody.TryGetProperty("topic", out var topicElement) ||
                    !requestBody.TryGetProperty("title", out var titleElement) ||
                    !requestBody.TryGetProperty("body", out var bodyElement))
                    return BadRequest(new { error = "Topic, title, and body are required" });

                var topic = topicElement.GetString();
                var title = titleElement.GetString();
                var body = bodyElement.GetString();

                var dataPayload = new Dictionary<string, string>();
                if (requestBody.TryGetProperty("data", out var dataElement))
                {
                    foreach (var prop in dataElement.EnumerateObject())
                    {
                        dataPayload[prop.Name] = prop.Value.ToString();
                    }
                }

                var message = new Message()
                {
                    Topic = topic,
                    Notification = new Notification()
                    {
                        Title = title,
                        Body = body
                    },
                    Data = dataPayload,
                    Android = new AndroidConfig()
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification()
                        {
                            ChannelId = "stylu_channel",
                            Sound = "default",
                            Priority = NotificationPriority.MAX
                        }
                    }
                };

                var response = await _messaging.SendAsync(message);

                return Ok(new
                {
                    success = true,
                    message = "Topic notification sent successfully",
                    messageId = response
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to send topic notification", details = ex.Message });
            }
        }

        // Helper: Mark token as inactive
        private async Task MarkTokenInactive(string fcmToken, string authToken, string supabaseUrl, string supabaseKey)
        {
            try
            {
                var updateData = new { is_active = false };
                var request = new HttpRequestMessage(HttpMethod.Patch,
                    $"{supabaseUrl}/rest/v1/device_tokens?fcm_token=eq.{fcmToken}")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(updateData),
                        Encoding.UTF8,
                        "application/json"
                    )
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
                request.Headers.Add("apikey", supabaseKey);

                await _httpClient.SendAsync(request);
            }
            catch { /* Ignore errors */ }
        }

        // Helper: Extract user ID from JWT
        private string? ExtractUserIdFromToken(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3) return null;

                var payload = parts[1];
                var paddedPayload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
                var jsonBytes = Convert.FromBase64String(paddedPayload);
                var json = Encoding.UTF8.GetString(jsonBytes);
                var doc = JsonDocument.Parse(json);

                return doc.RootElement.GetProperty("sub").GetString();
            }
            catch
            {
                return null;
            }
        }
    }
}