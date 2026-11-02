// Copyright (c) 2026 Bilal Raza
// Publisher: RAZ Studio
// Product: Nyxara AI Studio

using System.Text.RegularExpressions;

namespace Nyxara.AICompanion.Parsing
{
    public static class DialogueSanitizer
    {
        private static readonly Regex AsterisksRegex = new(@"\*[^*]*\*", RegexOptions.Compiled);
        private static readonly Regex BracketsRegex = new(@"\[[^\[\]]*\]", RegexOptions.Compiled);
        private static readonly Regex ParenthesesRegex = new(@"\([^()]*\)", RegexOptions.Compiled);
        private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);

        public static string Sanitize(string dialogue)
        {
            if (string.IsNullOrWhiteSpace(dialogue))
            {
                return string.Empty;
            }

            var cleaned = dialogue;
            cleaned = AsterisksRegex.Replace(cleaned, "");
            cleaned = BracketsRegex.Replace(cleaned, "");
            cleaned = ParenthesesRegex.Replace(cleaned, "");
            cleaned = MultipleSpacesRegex.Replace(cleaned, " ");
            cleaned = cleaned.Trim();

            string[] prefixes = { "*", "- ", "> ", "| " };
            foreach (var prefix in prefixes)
            {
                if (cleaned.StartsWith(prefix))
                {
                    cleaned = cleaned[prefix.Length..].TrimStart();
                }
            }

            return cleaned;
        }
    }
}
