using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Stylu.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // This requires a valid JWT token
    public class UserController : ControllerBase
    {
        [HttpGet("profile")]
        public IActionResult GetProfile()
        {
            try
            {
                // Extract user information from Supabase JWT claims
                var userId = User.FindFirst("sub")?.Value; // Supabase uses 'sub' for user ID
                var email = User.FindFirst("email")?.Value;
                var role = User.FindFirst("role")?.Value;
                var aud = User.FindFirst("aud")?.Value;
                var iss = User.FindFirst("iss")?.Value;

                // Get user metadata from app_metadata or user_metadata claims
                var appMetadata = User.FindFirst("app_metadata")?.Value;
                var userMetadata = User.FindFirst("user_metadata")?.Value;

                // Get all claims for debugging
                var allClaims = User.Claims.Select(c => new {
                    Type = c.Type,
                    Value = c.Value
                }).ToArray();

                return Ok(new
                {
                    Success = true,
                    UserId = userId,
                    Email = email,
                    Role = role,
                    Audience = aud,
                    Issuer = iss,
                    AppMetadata = appMetadata,
                    UserMetadata = userMetadata,
                    AllClaims = allClaims // Helpful for debugging
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "Failed to get user profile",
                    Message = ex.Message
                });
            }
        }

        [HttpGet("data")]
        public IActionResult GetUserData()
        {
            try
            {
                // Your custom business logic here
                var userId = User.FindFirst("sub")?.Value;
                var email = User.FindFirst("email")?.Value;

                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new
                    {
                        Success = false,
                        Error = "User ID not found in token"
                    });
                }

                // Example: Fetch user-specific data from your database
                // In a real application, you would query your database here
                var userData = new
                {
                    Success = true,
                    UserId = userId,
                    Email = email,
                    CustomData = "This is your custom business data",
                    UserPreferences = new
                    {
                        Theme = "dark",
                        Language = "en",
                        Notifications = true
                    },
                    LastLogin = DateTime.UtcNow.AddHours(-2),
                    Timestamp = DateTime.UtcNow
                };

                return Ok(userData);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    Success = false,
                    Error = "Failed to get user data",
                    Message = ex.Message
                });
            }
        }

        [HttpGet("test")]
        [AllowAnonymous] // This endpoint doesn't require authentication
        public IActionResult TestEndpoint()
        {
            return Ok(new
            {
                Success = true,
                Message = "API is working correctly",
                Timestamp = DateTime.UtcNow,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            });
        }
    }
}