using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ResumeScannerApp.Utilities.Validators
{
    public static class ContactValidator
    {
        private static Regex EmailRx = new(@"^[\w\.\-]+@[\w\.\-]+\.\w{2,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex PhoneRx = new(@"^(\+?\d{1,3}[\s\-\.])?[\d\-\(\)\s]{6,}$", RegexOptions.Compiled);

        public static bool IsValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && EmailRx.IsMatch(email);

        public static bool IsValidPhone(string? phone) => !string.IsNullOrWhiteSpace(phone) && PhoneRx.IsMatch(phone);
    }
}
