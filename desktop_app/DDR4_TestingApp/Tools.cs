using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DDR4_TestingApp
{
    internal static class Tools
    {
        public static string FormatBytes(ulong bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int index = 0;

            while (size >= 1024 && index < suffixes.Length - 1)
            {
                size /= 1024;
                index++;
            }

            // Show decimals only when needed
            string formatted = (size % 1 == 0)
                ? $"{(uint)size} {suffixes[index]}"
                : $"{size:F1} {suffixes[index]}";

            return formatted;
        }

        public static string EpochToReadable(ulong epochSeconds)
        {
            DateTimeOffset dt = DateTimeOffset.FromUnixTimeSeconds((long)epochSeconds);
            return dt.LocalDateTime.ToString("MM/dd/yy hh:mm:ss tt");
            //return dt.LocalDateTime.ToString("hh:mm:ss tt");
        }

        public static string FormatFixed(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "".PadRight(maxLength);
            return value.Length <= maxLength
                ? value.PadRight(maxLength)
                : value.Substring(0, maxLength);
        }

        public static string SelectFolder(string initialPath = "")
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder";
                dialog.UseDescriptionForTitle = true;
                dialog.ShowNewFolderButton = true;
                dialog.InitialDirectory = initialPath;

                return dialog.ShowDialog() == DialogResult.OK
                    ? dialog.SelectedPath
                    : string.Empty;
            }
        }


        /// <summary>
        /// Parses a string like "0x0F" or "0X1a2B" into its uint32 value.
        /// Returns null if it's not exactly "0x"/"0X" followed by 1-8 valid hex
        /// digits representing a value that fits in a uint (no sign, no
        /// whitespace, no other characters).
        /// </summary>
        public static uint? ParseHex(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            if (input.Length <= 2)
                return null;

            if (input[0] != '0' || (input[1] != 'x' && input[1] != 'X'))
                return null;

            string digits = input.Substring(2);

            if (uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
                return value;

            return null;
        }

        /// <summary>
        /// Formats a uint as "0xXXXXXXXX" - always exactly 8 hex digits,
        /// zero-padded, uppercase, with a "0x" prefix.
        /// </summary>
        public static string ToHexString(uint value)
        {
            return "0x" + value.ToString("X8");
        }
    }
}
