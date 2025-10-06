using System;
namespace Stylu.Models
{
    public class SignUpRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string PhoneNumber { get; set; } // optional
    }
}

