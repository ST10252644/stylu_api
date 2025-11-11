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
    public class CalendarController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public CalendarController(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }
[HttpGet("debug/check-schedules")]
public async Task<IActionResult> DebugCheckSchedules(
    [FromQuery] string? startDate = null,
    [FromQuery] string? endDate = null)
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

    try
    {
        // Query 1: Get ALL schedules for this user (no date filter)
        var allSchedulesUrl = $"{supabaseUrl}/rest/v1/outfit_schedule?" +
            $"user_id=eq.{userId}&" +
            $"select=schedule_id,user_id,outfit_id,event_date,event_name,notes&" +
            $"order=event_date.asc";

        var allSchedulesRequest = new HttpRequestMessage(HttpMethod.Get, allSchedulesUrl);
        allSchedulesRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        allSchedulesRequest.Headers.Add("apikey", supabaseKey);

        var allSchedulesResponse = await _httpClient.SendAsync(allSchedulesRequest);
        var allSchedulesBody = await allSchedulesResponse.Content.ReadAsStringAsync();

        var debugInfo = new
        {
            userId = userId,
            queriedStartDate = startDate ?? "not specified",
            queriedEndDate = endDate ?? "not specified",
            allSchedulesQuery = allSchedulesUrl,
            allSchedulesStatusCode = (int)allSchedulesResponse.StatusCode,
            allSchedulesCount = allSchedulesResponse.IsSuccessStatusCode 
                ? JsonDocument.Parse(allSchedulesBody).RootElement.GetArrayLength() 
                : 0,
            allSchedulesData = allSchedulesResponse.IsSuccessStatusCode 
                ? JsonDocument.Parse(allSchedulesBody).RootElement 
                : JsonDocument.Parse("[]").RootElement
        };

        // Query 2: If dates specified, query with date filter
        if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
        {
            var filteredUrl = $"{supabaseUrl}/rest/v1/outfit_schedule?" +
                $"user_id=eq.{userId}&" +
                $"event_date=gte.{startDate}&" +
                $"event_date=lte.{endDate}&" +
                $"select=schedule_id,user_id,outfit_id,event_date,event_name,notes&" +
                $"order=event_date.asc";

            var filteredRequest = new HttpRequestMessage(HttpMethod.Get, filteredUrl);
            filteredRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            filteredRequest.Headers.Add("apikey", supabaseKey);

            var filteredResponse = await _httpClient.SendAsync(filteredRequest);
            var filteredBody = await filteredResponse.Content.ReadAsStringAsync();

            return Ok(new
            {
                debugInfo,
                filteredQuery = new
                {
                    url = filteredUrl,
                    statusCode = (int)filteredResponse.StatusCode,
                    matchCount = filteredResponse.IsSuccessStatusCode
                        ? JsonDocument.Parse(filteredBody).RootElement.GetArrayLength()
                        : 0,
                    data = filteredResponse.IsSuccessStatusCode
                        ? JsonDocument.Parse(filteredBody).RootElement
                        : JsonDocument.Parse("[]").RootElement
                }
            });
        }

        return Ok(debugInfo);
    }
    catch (Exception ex)
    {
        return BadRequest(new { error = "Debug query failed", details = ex.Message, stackTrace = ex.StackTrace });
    }
}



        /// <summary>
        /// Schedule an outfit for a specific date
        /// POST /api/Calendar/schedule
        /// </summary>
        [HttpPost("schedule")]
        public async Task<IActionResult> ScheduleOutfit([FromBody] JsonElement requestBody)
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

            try
            {
                // Parse request body
                var outfitId = requestBody.GetProperty("outfitId").GetInt32();
                var eventDate = requestBody.GetProperty("eventDate").GetString();
                var eventName = requestBody.TryGetProperty("eventName", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var notes = requestBody.TryGetProperty("notes", out var notesElement)
                    ? notesElement.GetString()
                    : null;

                // Create schedule record
                var scheduleData = new
                {
                    user_id = userId,
                    outfit_id = outfitId,
                    event_date = eventDate,
                    event_name = eventName,
                    notes = notes
                };

                var jsonContent = JsonSerializer.Serialize(scheduleData);
                var request = new HttpRequestMessage(HttpMethod.Post,
                    $"{supabaseUrl}/rest/v1/outfit_schedule")
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Prefer", "return=representation");

                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode,
                        new { error = "Failed to schedule outfit", details = body });

                var scheduleArray = JsonDocument.Parse(body).RootElement;
                var createdSchedule = scheduleArray[0];

                return StatusCode(201, new
                {
                    scheduleId = createdSchedule.GetProperty("schedule_id").GetInt32(),
                    userId = createdSchedule.GetProperty("user_id").GetString(),
                    outfitId = createdSchedule.GetProperty("outfit_id").GetInt32(),
                    eventDate = createdSchedule.GetProperty("event_date").GetString(),
                    eventName = createdSchedule.TryGetProperty("event_name", out var en) ? en.GetString() : null,
                    notes = createdSchedule.TryGetProperty("notes", out var n) ? n.GetString() : null
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Invalid request data", details = ex.Message });
            }
        }

        /// <summary>
        /// Get scheduled outfits within a date range
        /// GET /api/Calendar/scheduled?startDate=2024-01-01&endDate=2024-01-31
        /// </summary>
       
[HttpGet("scheduled")]
public async Task<IActionResult> GetScheduledOutfits(
    [FromQuery] string startDate,
    [FromQuery] string endDate)
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

    try
    {
        // Step 1: Get scheduled outfits
        var scheduleUrl = $"{supabaseUrl}/rest/v1/outfit_schedule?" +
            $"user_id=eq.{userId}&" +
            $"event_date=gte.{startDate}&" +
            $"event_date=lte.{endDate}&" +
            $"select=schedule_id,user_id,outfit_id,event_date,event_name,notes&" +
            $"order=event_date.asc";

        Console.WriteLine($"📅 Step 1: Fetching schedules - {scheduleUrl}");

        var scheduleRequest = new HttpRequestMessage(HttpMethod.Get, scheduleUrl);
        scheduleRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        scheduleRequest.Headers.Add("apikey", supabaseKey);

        var scheduleResponse = await _httpClient.SendAsync(scheduleRequest);
        var scheduleBody = await scheduleResponse.Content.ReadAsStringAsync();

        Console.WriteLine($"📥 Step 1 Response Code: {scheduleResponse.StatusCode}");
        Console.WriteLine($"📥 Step 1 Response Body: {scheduleBody}");

        if (!scheduleResponse.IsSuccessStatusCode)
            return StatusCode((int)scheduleResponse.StatusCode,
                new { error = "Failed to fetch schedules", details = scheduleBody });

        var schedules = JsonDocument.Parse(scheduleBody).RootElement;
        Console.WriteLine($"📊 Found {schedules.GetArrayLength()} schedules");

        var result = new List<object>();

        // Step 2: For each schedule, fetch the outfit details
        foreach (var schedule in schedules.EnumerateArray())
        {
            var outfitId = schedule.GetProperty("outfit_id").GetInt32();
            Console.WriteLine($"👕 Step 2: Fetching outfit {outfitId} for user {userId}");

            var outfitUrl = $"{supabaseUrl}/rest/v1/outfit?" +
                $"outfit_id=eq.{outfitId}&" +
                $"user_id=eq.{userId}&" +
                $"select=outfit_id,outfit_name,category," +
                $"outfit_item(" +
                    $"item_id," +
                    $"layout_data," +
                    $"item:item_id(" +
                        $"item_id," +
                        $"item_name," +
                        $"image_url," +
                        $"colour," +
                        $"sub_category:subcategory_id(name)" +
                    $")" +
                $")";

            Console.WriteLine($"🔗 Outfit Query URL: {outfitUrl}");

            var outfitRequest = new HttpRequestMessage(HttpMethod.Get, outfitUrl);
            outfitRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            outfitRequest.Headers.Add("apikey", supabaseKey);

            var outfitResponse = await _httpClient.SendAsync(outfitRequest);
            var outfitBody = await outfitResponse.Content.ReadAsStringAsync();

            Console.WriteLine($"📥 Outfit Response Code: {outfitResponse.StatusCode}");
            Console.WriteLine($"📥 Outfit Response Body: {outfitBody}");

            if (!outfitResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"❌ Failed to fetch outfit {outfitId}: {outfitBody}");
                continue; // Skip if outfit not found or deleted
            }

            var outfits = JsonDocument.Parse(outfitBody).RootElement;
            if (outfits.GetArrayLength() == 0)
            {
                Console.WriteLine($"⚠️ No outfit found with ID {outfitId} for user {userId}");
                continue; // Skip if no outfit found
            }

            var outfit = outfits[0];
            var items = new List<object>();

            // Parse outfit items
            if (outfit.TryGetProperty("outfit_item", out var outfitItems))
            {
                Console.WriteLine($"🎨 Processing {outfitItems.GetArrayLength()} items");
                
                foreach (var outfitItem in outfitItems.EnumerateArray())
                {
                    if (outfitItem.TryGetProperty("item", out var item) && 
                        item.ValueKind != JsonValueKind.Null)
                    {
                        // Get subcategory name from nested object
                        var subcategoryName = "";
                        if (item.TryGetProperty("sub_category", out var subCat) && 
                            subCat.ValueKind != JsonValueKind.Null)
                        {
                            subcategoryName = subCat.TryGetProperty("name", out var scName) 
                                ? scName.GetString() ?? "" 
                                : "";
                        }

                        // Parse layoutData if present
                        object? layoutDataObj = null;
                        if (outfitItem.TryGetProperty("layout_data", out var ld) && 
                            ld.ValueKind != JsonValueKind.Null)
                        {
                            layoutDataObj = new
                            {
                                x = ld.TryGetProperty("x", out var x) ? x.GetDouble() : 0.0,
                                y = ld.TryGetProperty("y", out var y) ? y.GetDouble() : 0.0,
                                scale = ld.TryGetProperty("scale", out var scale) ? scale.GetDouble() : 1.0,
                                width = ld.TryGetProperty("width", out var w) ? w.GetInt32() : 100,
                                height = ld.TryGetProperty("height", out var h) ? h.GetInt32() : 100
                            };
                        }

                        items.Add(new
                        {
                            itemId = item.GetProperty("item_id").GetInt32(),
                            name = item.TryGetProperty("item_name", out var n) ? n.GetString() ?? "" : "",
                            imageUrl = item.TryGetProperty("image_url", out var url) ? url.GetString() ?? "" : "",
                            colour = item.TryGetProperty("colour", out var col) ? col.GetString() : null,
                            subcategory = subcategoryName,
                            layoutData = layoutDataObj
                        });
                    }
                }
            }
            else
            {
                Console.WriteLine($"⚠️ Outfit {outfitId} has no outfit_item array");
            }

            Console.WriteLine($"✅ Successfully processed outfit {outfitId} with {items.Count} items");

            result.Add(new
            {
                scheduleId = schedule.GetProperty("schedule_id").GetInt32(),
                eventDate = schedule.GetProperty("event_date").GetString(),
                outfit = new
                {
                    outfitId = outfit.GetProperty("outfit_id").GetInt32(),
                    name = outfit.GetProperty("outfit_name").GetString() ?? "",
                    category = outfit.TryGetProperty("category", out var cat) ? cat.GetString() ?? "" : "",
                    items = items
                },
                eventName = schedule.TryGetProperty("event_name", out var en) ? en.GetString() : null,
                notes = schedule.TryGetProperty("notes", out var nt) ? nt.GetString() : null,
                weather = (object?)null
            });
        }

        Console.WriteLine($"🎉 Returning {result.Count} scheduled outfits");
        return Ok(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ EXCEPTION: {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return BadRequest(new { error = "Invalid request", details = ex.Message });
    }
}


        
        /// <summary>
        /// Delete a scheduled outfit
        /// DELETE /api/Calendar/schedule/{id}
        /// </summary>
        [HttpDelete("schedule/{id}")]
        public async Task<IActionResult> DeleteSchedule(int id)
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
                $"{supabaseUrl}/rest/v1/outfit_schedule?schedule_id=eq.{id}&user_id=eq.{userId}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("apikey", supabaseKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode,
                    new { error = "Failed to delete schedule", details = body });
            }

            return Ok(new { message = "Schedule deleted successfully" });
        }

        /// <summary>
        /// Update a scheduled outfit
        /// PUT /api/Calendar/schedule/{id}
        /// </summary>
        [HttpPut("schedule/{id}")]
        public async Task<IActionResult> UpdateSchedule(int id, [FromBody] JsonElement requestBody)
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

            try
            {
                var updateData = new Dictionary<string, object?>();

                if (requestBody.TryGetProperty("eventDate", out var date))
                    updateData["event_date"] = date.GetString();

                if (requestBody.TryGetProperty("eventName", out var name))
                    updateData["event_name"] = name.GetString();

                if (requestBody.TryGetProperty("notes", out var notes))
                    updateData["notes"] = notes.GetString();

                if (requestBody.TryGetProperty("outfitId", out var outfitId))
                    updateData["outfit_id"] = outfitId.GetInt32();

                var jsonContent = JsonSerializer.Serialize(updateData);
                var request = new HttpRequestMessage(HttpMethod.Patch,
                    $"{supabaseUrl}/rest/v1/outfit_schedule?schedule_id=eq.{id}&user_id=eq.{userId}")
                {
                    Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("apikey", supabaseKey);
                request.Headers.Add("Prefer", "return=representation");

                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode,
                        new { error = "Failed to update schedule", details = body });

                return Ok(new { message = "Schedule updated successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Invalid request data", details = ex.Message });
            }
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
