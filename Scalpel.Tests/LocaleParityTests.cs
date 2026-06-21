using System.IO;
using System.Linq;
using Xunit;

namespace Scalpel.Tests
{
    public class LocaleParityTests
    {
        private static readonly string[] Locales =
            ["en-US", "es", "zh-TW", "zh-CN", "bn", "tr-TR"];

        private static readonly string[] NewKeys =
            ["Str_Mode_About", "Str_About_Author", "Str_About_Repository", "Str_About_License"];

        // Walk up from the test bin dir to the repo root (the folder that has a Strings dir).
        private static string StringsDir()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Strings")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "Strings");
        }

        [Theory]
        [InlineData("Str_Mode_About")]
        [InlineData("Str_About_Author")]
        [InlineData("Str_About_Repository")]
        [InlineData("Str_About_License")]
        public void New_about_keys_exist_in_every_locale(string key)
        {
            var marker = $"x:Key=\"{key}\"";
            foreach (var loc in Locales)
            {
                var text = File.ReadAllText(Path.Combine(StringsDir(), $"{loc}.xaml"));
                Assert.True(text.Contains(marker), $"{loc}.xaml is missing {key}");
            }
        }
    }
}
