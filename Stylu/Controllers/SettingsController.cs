using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Stylu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SettingsController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public SettingsController(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        // GET: api/Settings/profile
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
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

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{supabaseUrl}/rest/v1/user_profiles?id=eq.{userId}&select=*");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch profile" });

            var jsonArray = JsonDocument.Parse(body).RootElement;
            if (jsonArray.GetArrayLength() == 0)
                return NotFound(new { error = "Profile not found" });

            var profile = jsonArray[0];

            // Convert snake_case to camelCase for Android
            var responseData = new
            {
                firstName = profile.GetProperty("first_name").GetString(),
                lastName = profile.GetProperty("last_name").GetString(),
                phoneNumber = GetPropertyOrNull(profile, "phone_number"),
                email = profile.GetProperty("email").GetString(),
                language = GetPropertyOrNull(profile, "language") ?? "en",
                temperatureUnit = GetPropertyOrNull(profile, "temperature_unit") ?? "C",
                defaultReminderTime = GetPropertyOrNull(profile, "default_reminder_time") ?? "07:00",
                weatherSensitivity = GetPropertyOrNull(profile, "weather_sensitivity") ?? "normal",
                notifyWeather = GetBoolProperty(profile, "notify_weather", true),
                notifyOutfitReminders = GetBoolProperty(profile, "notify_outfit_reminders", true)
            };

            return Ok(responseData);
        }

        // PUT: api/Settings/profile
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] JsonElement requestBody)
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

            // Convert camelCase to snake_case for Supabase
            var supabaseData = new
            {
                first_name = requestBody.GetProperty("firstName").GetString(),
                last_name = requestBody.GetProperty("lastName").GetString(),
                phone_number = GetPropertyOrNull(requestBody, "phoneNumber"),
                email = requestBody.GetProperty("email").GetString(),
                updated_at = "now()"
            };

            var jsonContent = JsonSerializer.Serialize(supabaseData);
            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"{supabaseUrl}/rest/v1/user_profiles?id=eq.{userId}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);
            request.Headers.Add("Prefer", "return=minimal");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { error = "Failed to update profile" });
            }

            // Handle password update if provided
            if (requestBody.TryGetProperty("password", out var passwordElement))
            {
                var password = passwordElement.GetString();
                if (!string.IsNullOrEmpty(password))
                {
                    await UpdatePassword(token, password);
                }
            }

            return Ok(new { message = "Profile updated successfully" });
        }

        // GET: api/Settings/system
        [HttpGet("system")]
        public async Task<IActionResult> GetSystemSettings()
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

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{supabaseUrl}/rest/v1/user_profiles?id=eq.{userId}&select=language,temperature_unit,default_reminder_time,weather_sensitivity,notify_weather,notify_outfit_reminders");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch system settings" });

            var jsonArray = JsonDocument.Parse(body).RootElement;
            if (jsonArray.GetArrayLength() == 0)
                return NotFound(new { error = "Settings not found" });

            var settings = jsonArray[0];

            // Convert snake_case to camelCase
            var responseData = new
            {
                language = GetPropertyOrNull(settings, "language") ?? "en",
                temperatureUnit = GetPropertyOrNull(settings, "temperature_unit") ?? "C",
                defaultReminderTime = GetPropertyOrNull(settings, "default_reminder_time") ?? "07:00",
                weatherSensitivity = GetPropertyOrNull(settings, "weather_sensitivity") ?? "normal",
                notifyWeather = GetBoolProperty(settings, "notify_weather", true),
                notifyOutfitReminders = GetBoolProperty(settings, "notify_outfit_reminders", true)
            };

            return Ok(responseData);
        }

        // PUT: api/Settings/system
        [HttpPut("system")]
        public async Task<IActionResult> UpdateSystemSettings([FromBody] JsonElement requestBody)
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

            // Convert camelCase to snake_case for Supabase
            var supabaseData = new
            {
                language = requestBody.GetProperty("language").GetString(),
                temperature_unit = requestBody.GetProperty("temperatureUnit").GetString(),
                default_reminder_time = requestBody.GetProperty("defaultReminderTime").GetString(),
                weather_sensitivity = requestBody.GetProperty("weatherSensitivity").GetString(),
                notify_weather = requestBody.GetProperty("notifyWeather").GetBoolean(),
                notify_outfit_reminders = requestBody.GetProperty("notifyOutfitReminders").GetBoolean(),
                updated_at = "now()"
            };

            var jsonContent = JsonSerializer.Serialize(supabaseData);
            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"{supabaseUrl}/rest/v1/user_profiles?id=eq.{userId}")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);
            request.Headers.Add("Prefer", "return=minimal");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { error = "Failed to update system settings" });
            }

            return Ok(new { message = "System settings updated successfully" });
        }

        // Helper method to update password
        private async Task UpdatePassword(string token, string newPassword)
        {
            var supabaseUrl = _config["Supabase:Url"];
            var supabaseKey = _config["Supabase:AnonKey"];

            var passwordData = new { password = newPassword };
            var jsonContent = JsonSerializer.Serialize(passwordData);

            var request = new HttpRequestMessage(HttpMethod.Put,
                $"{supabaseUrl}/auth/v1/user")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            await _httpClient.SendAsync(request);
        }

        // Helper method to extract user ID from JWT token
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

       
        private string? GetPropertyOrNull(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
            }
            return null;
        }

        
        private bool GetBoolProperty(JsonElement element, string propertyName, bool defaultValue)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                return prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False
                    ? prop.GetBoolean()
                    : defaultValue;
            }
            return defaultValue;
        }
    }
}