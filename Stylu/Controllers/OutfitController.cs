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
    public class OutfitController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public OutfitController(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> GetOutfits()
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

            var requestUrl = $"{supabaseUrl}/rest/v1/outfit?user_id=eq.{userId}&select=*,outfit_item(item_id,layout_data,item(*))";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch outfits", details = body });

            return Ok(JsonDocument.Parse(body).RootElement);
        }

 [HttpPost]
        public async Task<IActionResult> CreateOutfit([FromBody] JsonElement requestBody)
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

            // Extract schedule
            string? schedule = null;
            if (requestBody.TryGetProperty("schedule", out var scheduleElement))
            {
                schedule = scheduleElement.GetString();
                _logger.LogInformation($"Creating outfit with schedule: {schedule}");
            }

            // Create outfit
            var outfitData = new Dictionary<string, object?>
    {
        { "user_id", userId },
        { "outfit_name", requestBody.GetProperty("name").GetString() }
    };

            if (!string.IsNullOrEmpty(schedule))
            {
                outfitData["schedule"] = schedule;
            }

            var outfitJsonContent = JsonSerializer.Serialize(outfitData);
            _logger.LogInformation($"Sending to Supabase: {outfitJsonContent}");

            var outfitRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{supabaseUrl}/rest/v1/outfit")
            {
                Content = new StringContent(outfitJsonContent, Encoding.UTF8, "application/json")
            };

            outfitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            outfitRequest.Headers.Add("apikey", supabaseKey);
            outfitRequest.Headers.Add("Prefer", "return=representation");

            var outfitResponse = await _httpClient.SendAsync(outfitRequest);
            var outfitBody = await outfitResponse.Content.ReadAsStringAsync();

            _logger.LogInformation($"Supabase response: {outfitBody}");

            if (!outfitResponse.IsSuccessStatusCode)
                return StatusCode((int)outfitResponse.StatusCode, new { error = $"Failed to create outfit: {outfitBody}" });

            var outfitArray = JsonDocument.Parse(outfitBody).RootElement;
            var outfitId = outfitArray[0].GetProperty("outfit_id").GetInt32();

            // ✅ FIX: Handle BOTH "items" (with layout) AND "itemIds" (simple array)
            if (requestBody.TryGetProperty("items", out var items))
            {
                var itemsList = new List<object>();

                // Check if it's a simple array of IDs or objects with layout
                var firstItem = items.EnumerateArray().FirstOrDefault();

                if (firstItem.ValueKind == JsonValueKind.Number)
                {
                    // ✅ SIMPLE ARRAY: [52, 54, 65, 66]
                    _logger.LogInformation("Processing simple item IDs array");

                    foreach (var itemId in items.EnumerateArray())
                    {
                        itemsList.Add(new
                        {
                            outfit_id = outfitId,
                            item_id = itemId.GetInt32()
                            // No layout_data - will be null
                        });
                    }
                }
                else
                {
                    // ✅ COMPLEX ARRAY: Objects with layout data
                    _logger.LogInformation("Processing items with layout data");

                    foreach (var item in items.EnumerateArray())
                    {
                        var layoutData = new
                        {
                            x = item.GetProperty("x").GetDouble(),
                            y = item.GetProperty("y").GetDouble(),
                            scale = item.GetProperty("scale").GetDouble(),
                            width = item.GetProperty("width").GetInt32(),
                            height = item.GetProperty("height").GetInt32()
                        };

                        itemsList.Add(new
                        {
                            outfit_id = outfitId,
                            item_id = item.GetProperty("itemId").GetInt32(),
                            layout_data = layoutData
                        });
                    }
                }

                if (itemsList.Count > 0)
                {
                    var itemsJsonContent = JsonSerializer.Serialize(itemsList);
                    _logger.LogInformation($"Inserting {itemsList.Count} items: {itemsJsonContent}");

                    var itemsRequest = new HttpRequestMessage(HttpMethod.Post,
                        $"{supabaseUrl}/rest/v1/outfit_item")
                    {
                        Content = new StringContent(itemsJsonContent, Encoding.UTF8, "application/json")
                    };

                    itemsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    itemsRequest.Headers.Add("apikey", supabaseKey);

                    var itemsResponse = await _httpClient.SendAsync(itemsRequest);
                    var itemsBody = await itemsResponse.Content.ReadAsStringAsync();

                    _logger.LogInformation($"Items insert response: {itemsBody}");

                    if (!itemsResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Failed to insert items: {itemsBody}");
                        return StatusCode((int)itemsResponse.StatusCode, new { error = "Outfit created but failed to add items", details = itemsBody });
                    }

                    _logger.LogInformation($"✅ Successfully inserted {itemsList.Count} items");
                }
            }
            // ✅ ALSO CHECK FOR "itemIds" (what Android is currently sending)
            else if (requestBody.TryGetProperty("itemIds", out var itemIds))
            {
                _logger.LogInformation("Processing itemIds array");

                var itemsList = new List<object>();

                foreach (var itemId in itemIds.EnumerateArray())
                {
                    itemsList.Add(new
                    {
                        outfit_id = outfitId,
                        item_id = itemId.GetInt32()
                    });
                }

                if (itemsList.Count > 0)
                {
                    var itemsJsonContent = JsonSerializer.Serialize(itemsList);
                    _logger.LogInformation($"Inserting {itemsList.Count} items: {itemsJsonContent}");

                    var itemsRequest = new HttpRequestMessage(HttpMethod.Post,
                        $"{supabaseUrl}/rest/v1/outfit_item")
                    {
                        Content = new StringContent(itemsJsonContent, Encoding.UTF8, "application/json")
                    };

                    itemsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    itemsRequest.Headers.Add("apikey", supabaseKey);

                    var itemsResponse = await _httpClient.SendAsync(itemsRequest);
                    var itemsBody = await itemsResponse.Content.ReadAsStringAsync();

                    _logger.LogInformation($"Items insert response: {itemsBody}");

                    if (!itemsResponse.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Failed to insert items: {itemsBody}");
                        return StatusCode((int)itemsResponse.StatusCode, new { error = "Outfit created but failed to add items", details = itemsBody });
                    }

                    _logger.LogInformation($"✅ Successfully inserted {itemsList.Count} items");
                }
            }

            return Ok(new
            {
                message = "Outfit created successfully",
                outfitId = outfitId,
                data = outfitArray[0]
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateOutfit(int id, [FromBody] JsonElement requestBody)
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

            var outfitData = new
            {
                outfit_name = requestBody.GetProperty("name").GetString()
            };

            var outfitJsonContent = JsonSerializer.Serialize(outfitData);
            var outfitRequest = new HttpRequestMessage(HttpMethod.Patch,
                $"{supabaseUrl}/rest/v1/outfit?outfit_id=eq.{id}&user_id=eq.{userId}")
            {
                Content = new StringContent(outfitJsonContent, Encoding.UTF8, "application/json")
            };

            outfitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            outfitRequest.Headers.Add("apikey", supabaseKey);

            var outfitResponse = await _httpClient.SendAsync(outfitRequest);

            if (!outfitResponse.IsSuccessStatusCode)
                return StatusCode((int)outfitResponse.StatusCode, new { error = "Failed to update outfit" });

            var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                $"{supabaseUrl}/rest/v1/outfit_item?outfit_id=eq.{id}");

            deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            deleteRequest.Headers.Add("apikey", supabaseKey);

            await _httpClient.SendAsync(deleteRequest);

            if (requestBody.TryGetProperty("items", out var items))
            {
                var itemsList = new List<object>();
                foreach (var item in items.EnumerateArray())
                {
                    var layoutData = new
                    {
                        x = item.GetProperty("x").GetDouble(),
                        y = item.GetProperty("y").GetDouble(),
                        scale = item.GetProperty("scale").GetDouble(),
                        width = item.GetProperty("width").GetInt32(),
                        height = item.GetProperty("height").GetInt32()
                    };

                    itemsList.Add(new
                    {
                        outfit_id = id,
                        item_id = item.GetProperty("itemId").GetInt32(),
                        layout_data = layoutData
                    });
                }

                var itemsJsonContent = JsonSerializer.Serialize(itemsList);
                var itemsRequest = new HttpRequestMessage(HttpMethod.Post,
                    $"{supabaseUrl}/rest/v1/outfit_item")
                {
                    Content = new StringContent(itemsJsonContent, Encoding.UTF8, "application/json")
                };

                itemsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                itemsRequest.Headers.Add("apikey", supabaseKey);

                var itemsResponse = await _httpClient.SendAsync(itemsRequest);

                if (!itemsResponse.IsSuccessStatusCode)
                    return StatusCode((int)itemsResponse.StatusCode, new { error = "Failed to update items" });
            }

            return Ok(new { message = "Outfit updated successfully" });
        }

        [HttpGet("{id}/items")]
        public async Task<IActionResult> GetOutfitItems(int id)
        {
            var userToken = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(userToken))
                return Unauthorized(new { error = "Missing token" });

            var token = userToken.Replace("Bearer ", "");
            var supabaseUrl = _config["Supabase:Url"];
            var supabaseKey = _config["Supabase:AnonKey"];

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{supabaseUrl}/rest/v1/outfit_item?outfit_id=eq.{id}&select=*,item(*)");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to fetch outfit items" });

            return Ok(JsonDocument.Parse(body).RootElement);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteOutfit(int id)
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
                $"{supabaseUrl}/rest/v1/outfit?outfit_id=eq.{id}&user_id=eq.{userId}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, new { error = "Failed to delete outfit" });

            return Ok(new { message = "Outfit deleted successfully" });
        }

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
