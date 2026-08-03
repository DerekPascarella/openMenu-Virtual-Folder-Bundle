using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GDMENUCardManager.Core
{
    /// <summary>
    /// Title casing tuned for game titles: small words stay lowercase unless they lead, Roman
    /// numerals stay uppercase.
    /// </summary>
    public static class TitleCaseHelper
    {
        // Words that should remain lowercase (unless first/last or after colon)
        private static readonly HashSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Articles
            "a", "an", "the",
            // Conjunctions
            "and", "but", "or", "nor", "for", "yet", "so",
            // Short prepositions
            "as", "at", "by", "in", "of", "on", "to", "up", "vs", "via"
        };

        // Strict numeral grammar. Rejects sequences like IIII or VX.
        private static readonly Regex RomanNumeralRegex = new Regex(
            @"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string ToTitleCase(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var parts = SplitPreservingSpaces(input);
            var result = new StringBuilder();

            bool isFirstWord = true;
            bool afterColon = false;
            int lastWordIndex = FindLastWordIndex(parts);

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];

                // If it's just whitespace, add it directly
                if (string.IsNullOrWhiteSpace(part))
                {
                    result.Append(part);
                    continue;
                }

                bool isLastWord = (i == lastWordIndex);
                string processed = ProcessWord(part, isFirstWord, isLastWord, afterColon);
                result.Append(processed);

                // Check if this part ends with a colon
                afterColon = part.TrimEnd().EndsWith(":");
                isFirstWord = false;
            }

            return result.ToString();
        }

        private static List<string> SplitPreservingSpaces(string input)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool inSpace = false;

            foreach (char c in input)
            {
                bool isSpace = char.IsWhiteSpace(c);

                if (isSpace != inSpace && current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                current.Append(c);
                inSpace = isSpace;
            }

            if (current.Length > 0)
                parts.Add(current.ToString());

            return parts;
        }

        private static int FindLastWordIndex(List<string> parts)
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                    return i;
            }
            return -1;
        }

        private static string ProcessWord(string word, bool isFirst, bool isLast, bool afterColon)
        {
            if (word.Contains("-"))
            {
                return ProcessHyphenatedWord(word, isFirst, isLast, afterColon);
            }

            // Extract any leading/trailing punctuation
            int start = 0;
            int end = word.Length;

            while (start < word.Length && !char.IsLetterOrDigit(word[start]))
                start++;

            while (end > start && !char.IsLetterOrDigit(word[end - 1]))
                end--;

            if (start >= end)
                return word; // All punctuation, return as-is

            string prefix = word.Substring(0, start);
            string core = word.Substring(start, end - start);
            string suffix = word.Substring(end);

            string processedCore = ProcessCoreWord(core, isFirst, isLast, afterColon);

            return prefix + processedCore + suffix;
        }

        private static string ProcessHyphenatedWord(string word, bool isFirst, bool isLast, bool afterColon)
        {
            var parts = word.Split('-');
            var result = new StringBuilder();

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    result.Append('-');

                // The first part follows the normal rules. Later parts are always capitalized.
                bool partIsFirst = (i == 0 && isFirst);
                bool partIsLast = (i == parts.Length - 1 && isLast);
                bool alwaysCapitalize = (i > 0);

                if (alwaysCapitalize)
                    result.Append(Capitalize(parts[i]));
                else
                    result.Append(ProcessCoreWord(parts[i], partIsFirst, partIsLast, afterColon));
            }

            return result.ToString();
        }

        private static string ProcessCoreWord(string word, bool isFirst, bool isLast, bool afterColon)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            if (IsRomanNumeral(word))
            {
                return word.ToUpperInvariant();
            }

            // Check if it's a small word that should stay lowercase
            if (!isFirst && !isLast && !afterColon && SmallWords.Contains(word))
            {
                return word.ToLowerInvariant();
            }

            // Standard title case: capitalize first letter, lowercase the rest
            return Capitalize(word);
        }

        private static string Capitalize(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            if (word.Length == 1)
                return word.ToUpperInvariant();

            return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
        }

        private static bool IsRomanNumeral(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;

            if (!Regex.IsMatch(word, @"^[IVXLCDMivxlcdm]+$"))
                return false;

            // A lone "I" is treated as a numeral. In game titles that beats the pronoun reading.
            return RomanNumeralRegex.IsMatch(word);
        }
    }
}
