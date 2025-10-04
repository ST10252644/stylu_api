using System;
using System.Text.Json.Serialization;

namespace Stylu.Models
{
    public class SupabaseSignUpResponse
    {
        public SupabaseUser User { get; set; }
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
    }
}

