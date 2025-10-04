using System;
using System.Text.Json.Serialization;

namespace Stylu.Models
{
    public class SupabaseUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("email")]
        public string Email { get; set; }
    }
}

