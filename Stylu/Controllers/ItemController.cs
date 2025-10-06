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
    public class ItemController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public ItemController(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        // GET: api/Item/categories
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var userToken = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(userToken))
                return Unauthorized(new { error = "Missing token" });

            var token = userToken.Replace("Bearer ", "");
            var supabaseUrl = _config["Supabase:Url"];
            var supabaseKey = _config["Supabase:AnonKey"];

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{supabaseUrl}/rest/v1/category?select=*,sub_category(*)");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch categories" });

            return Ok(JsonDocument.Parse(body).RootElement);
        }

        // POST: api/Item
        [HttpPost]
        public async Task<IActionResult> CreateItem([FromBody] JsonElement requestBody)
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
                user_id = userId,
                subcategory_id = requestBody.GetProperty("subcategoryId").GetInt32(),
                name = GetPropertyOrNull(requestBody, "name"),
                colour = GetPropertyOrNull(requestBody, "colour"),
                material = GetPropertyOrNull(requestBody, "material"),
                size = GetPropertyOrNull(requestBody, "size"),
                price = requestBody.TryGetProperty("price", out var priceElement) && priceElement.ValueKind == JsonValueKind.Number
                    ? (double?)priceElement.GetDouble() : null,
                image_url = requestBody.GetProperty("imageUrl").GetString(),
                weather_tag = GetPropertyOrNull(requestBody, "weatherTag"),
                times_worn = 0,
                created_by = requestBody.GetProperty("createdBy").GetString()
            };

            var jsonContent = JsonSerializer.Serialize(supabaseData);
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{supabaseUrl}/rest/v1/item")
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);
            request.Headers.Add("Prefer", "return=representation");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to create item" });

            var jsonArray = JsonDocument.Parse(responseBody).RootElement;
            if (jsonArray.GetArrayLength() == 0)
                return StatusCode(500, new { error = "Item creation returned no data" });

            var item = jsonArray[0];

            // Convert snake_case to camelCase for response
            var responseData = new
            {
                success = true,
                message = "Item created successfully",
                data = new
                {
                    itemId = item.GetProperty("item_id").GetInt32(),
                    userId = item.GetProperty("user_id").GetString(),
                    subcategoryId = item.GetProperty("subcategory_id").GetInt32(),
                    name = GetPropertyOrNull(item, "name"),
                    colour = GetPropertyOrNull(item, "colour"),
                    material = GetPropertyOrNull(item, "material"),
                    size = GetPropertyOrNull(item, "size"),
                    price = item.TryGetProperty("price", out var p) && p.ValueKind == JsonValueKind.Number
                        ? (double?)p.GetDouble() : null,
                    imageUrl = item.GetProperty("image_url").GetString(),
                    weatherTag = GetPropertyOrNull(item, "weather_tag"),
                    timesWorn = item.GetProperty("times_worn").GetInt32(),
                    createdBy = item.GetProperty("created_by").GetString(),
                    createdAt = item.GetProperty("created_at").GetString()
                }
            };

            return CreatedAtAction(nameof(GetItemById), new { id = item.GetProperty("item_id").GetInt32() }, responseData);
        }

        // GET: api/Item
        [HttpGet]
        public async Task<IActionResult> GetUserItems()
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
                $"{supabaseUrl}/rest/v1/item?user_id=eq.{userId}&select=*,sub_category(name,category(name))");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch items" });

            return Ok(JsonDocument.Parse(body).RootElement);
        }

        // GET: api/Item/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetItemById(int id)
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
                $"{supabaseUrl}/rest/v1/item?item_id=eq.{id}&user_id=eq.{userId}&select=*,sub_category(name,category(name))");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch item" });

            var jsonArray = JsonDocument.Parse(body).RootElement;
            if (jsonArray.GetArrayLength() == 0)
                return NotFound(new { error = "Item not found" });

            return Ok(jsonArray[0]);
        }

        // GET: api/Item/counts
        [HttpGet("counts")]
        public async Task<IActionResult> GetItemCountsByCategory()
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
                $"{supabaseUrl}/rest/v1/item?user_id=eq.{userId}&select=sub_category(category(name))");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch item counts" });

            var items = JsonDocument.Parse(body).RootElement;
            var categoryCounts = new Dictionary<string, int>();

            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("sub_category", out var subCat) &&
                    subCat.TryGetProperty("category", out var category) &&
                    category.TryGetProperty("name", out var categoryName))
                {
                    var catName = categoryName.GetString();
                    if (!string.IsNullOrEmpty(catName))
                    {
                        categoryCounts[catName] = categoryCounts.GetValueOrDefault(catName, 0) + 1;
                    }
                }
            }

            return Ok(categoryCounts);
        }

        // PUT: api/Item/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateItem(int id, [FromBody] JsonElement requestBody)
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
            var supabaseData = new Dictionary<string, object>
            {
                ["updated_at"] = "now()"
            };

            if (requestBody.TryGetProperty("name", out var name))
                supabaseData["name"] = name.GetString();
            if (requestBody.TryGetProperty("colour", out var colour))
                supabaseData["colour"] = colour.GetString();
            if (requestBody.TryGetProperty("material", out var material))
                supabaseData["material"] = material.GetString();
            if (requestBody.TryGetProperty("size", out var size))
                supabaseData["size"] = size.GetString();
            if (requestBody.TryGetProperty("price", out var price) && price.ValueKind == JsonValueKind.Number)
                supabaseData["price"] = price.GetDouble();
            if (requestBody.TryGetProperty("weatherTag", out var weatherTag))
                supabaseData["weather_tag"] = weatherTag.GetString();

            var jsonContent = JsonSerializer.Serialize(supabaseData);
            var request = new HttpRequestMessage(HttpMethod.Patch,
                $"{supabaseUrl}/rest/v1/item?item_id=eq.{id}&user_id=eq.{userId}")
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
                return StatusCode((int)response.StatusCode, new { error = "Failed to update item" });
            }

            return Ok(new { message = "Item updated successfully" });
        }

        // DELETE: api/Item/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteItem(int id)
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

            var request = new HttpRequestMessage(HttpMethod.Delete,
                $"{supabaseUrl}/rest/v1/item?item_id=eq.{id}&user_id=eq.{userId}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, new { error = "Failed to delete item" });
            }

            return Ok(new { message = "Item deleted successfully" });
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
    }
}