//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using System.Net.Http.Headers;
//using System.Text;
//using System.Text.Json;
//using Dapper;
//using Stylu.Models;
//using System.Data;

//namespace Stylu.Controllers
//{
//    [ApiController]
//    [Route("api/[controller]")]
//    public class AuthController : ControllerBase
//    {
//        private readonly IDbConnection _db;
//        private readonly IConfiguration _config;
//        private readonly HttpClient _http;

//        public AuthController(IDbConnection db, IConfiguration config, IHttpClientFactory httpClientFactory)
//        {
//            _db = db;
//            _config = config;
//            _http = httpClientFactory.CreateClient();
//            _http.BaseAddress = new Uri(_config["Supabase:Url"]);
//            _http.DefaultRequestHeaders.Add("apikey", _config["Supabase:AnonKey"]);
//            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
//        }

//        [HttpPost("signup")]
//        [AllowAnonymous]
//        public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
//        {
//            try
//            {
//                var body = JsonSerializer.Serialize(new
//                {
//                    email = request.Email,
//                    password = request.Password,
//                    data = new
//                    {
//                        first_name = request.FirstName,
//                        last_name = request.LastName,
//                        phone_number = request.PhoneNumber
//                    }
//                });

//                var content = new StringContent(body, Encoding.UTF8, "application/json");
//                var response = await _http.PostAsync("/auth/v1/signup", content);
//                var responseString = await response.Content.ReadAsStringAsync();

//                if (!response.IsSuccessStatusCode)
//                {
//                    return BadRequest(new { Success = false, Error = responseString });
//                }

//                var supabaseResponse = JsonSerializer.Deserialize<SupabaseSignUpResponse>(responseString);

//                // Insert into user_profiles table (if needed)
//                await _db.ExecuteAsync(@"
//                    INSERT INTO user_profiles (id, email, first_name, last_name, phone_number)
//                    VALUES (@id, @Email, @FirstName, @LastName, @PhoneNumber)",
//                    new
//                    {
//                        id = supabaseResponse?.User?.Id,
//                        Email = request.Email,
//                        FirstName = request.FirstName,
//                        LastName = request.LastName,
//                        PhoneNumber = request.PhoneNumber
//                    });

//                return Ok(new
//                {
//                    Success = true,
//                    Token = supabaseResponse?.AccessToken,
//                    UserId = supabaseResponse?.User?.Id
//                });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new { Success = false, Error = "Signup failed", Message = ex.Message });
//            }
//        }

//        [HttpPost("signin")]
//        [AllowAnonymous]
//        public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
//        {
//            try
//            {
//                var body = JsonSerializer.Serialize(new
//                {
//                    email = request.Email,
//                    password = request.Password
//                });

//                var content = new StringContent(body, Encoding.UTF8, "application/json");
//                var response = await _http.PostAsync("/auth/v1/token?grant_type=password", content);
//                var responseString = await response.Content.ReadAsStringAsync();

//                if (!response.IsSuccessStatusCode)
//                {
//                    return Unauthorized(new { Success = false, Error = responseString });
//                }

//                var supabaseResponse = JsonSerializer.Deserialize<SupabaseSignInResponse>(responseString);

//                return Ok(new
//                {
//                    Success = true,
//                    Token = supabaseResponse?.AccessToken,
//                    RefreshToken = supabaseResponse?.RefreshToken,
//                    User = supabaseResponse?.User
//                });
//            }
//            catch (Exception ex)
//            {
//                return BadRequest(new
//                {
//                    Success = false,
//                    Error = "Signin fa
