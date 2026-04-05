using System.Text.RegularExpressions;

namespace Veda.Core.Extensions
{
    public static class StringsExtensions
    {
        /// <summary>
        /// Determines if the input string contains Chinese characters.
        /// </summary>
        /// <param name="text">The input string to check.</param>
        /// <returns>True if the string contains Chinese characters; otherwise, false.</returns>
        public static bool IsChinese(this string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Unicode range for CJK Unified Ideographs
            return Regex.IsMatch(text, "[\u4e00-\u9fff]");
        }
    }
}
