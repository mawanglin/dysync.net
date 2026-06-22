using dy.net.utils;

namespace dy.net.Tests
{
    public class DouyinFileNameHelperTests
    {
        [Theory]
        [InlineData("a/b:c*?\"<>|\\d", "def", false)]
        [InlineData("   ", "FallBack Name", false)]
        [InlineData("正常名字 123", "def", true)]
        [InlineData("line\nbreak\ttab", "def", false)]
        public void SanitizeLinuxFileName_locks_current_behavior(string input, string def, bool folder)
        {
            var actual = DouyinFileNameHelper.SanitizeLinuxFileName(input, def, folder);
            Assert.Equal(Golden(input, def, folder), actual);
        }

        private static string Golden(string i, string d, bool f) => (i, d, f) switch
        {
            ("a/b:c*?\"<>|\\d", "def", false) => "a_b_c_______d",
            ("   ", "FallBack Name", false) => "FallBackName",
            ("正常名字 123", "def", true) => "正常名字123",
            ("line\nbreak\ttab", "def", false) => "line_break\ttab",
            _ => throw new System.ArgumentException()
        };

        // 加固：保证任何 null/空 入参都不抛异常，且永不返回 null/空白
        // （根因修复：旧逻辑在 originalName 空且 defaultName 为 null 时会 NRE 或返回 null，
        //  导致下游 CreateSaveFolder 的 Path.Combine 抛 ArgumentNullException，使所有视频下载崩溃）
        [Theory]
        [InlineData(null, null, false)]
        [InlineData(null, null, true)]
        [InlineData("", null, true)]
        [InlineData("   ", null, false)]
        [InlineData("***", null, true)]   // 全非法字符 + isfolder 清洗后为空，defaultName 也为 null
        public void SanitizeLinuxFileName_never_returns_null_or_throws(string input, string def, bool folder)
        {
            var actual = DouyinFileNameHelper.SanitizeLinuxFileName(input, def, folder);
            Assert.False(string.IsNullOrWhiteSpace(actual));
        }

        [Fact]
        public void SanitizeLinuxFileName_falls_back_to_default_when_name_empty()
        {
            // originalName 清洗为空但 defaultName 有值 → 用 defaultName（AwemeId 兜底语义不变）
            Assert.Equal("7321309610927770930",
                DouyinFileNameHelper.SanitizeLinuxFileName("***", "7321309610927770930", true));
        }

        [Theory]
        [InlineData("abc定义123!!", "abc定义123")]
        [InlineData("  spaced  ", "spaced")]
        public void KeepChineseLettersAndNumbers_locks(string input, string expected)
            => Assert.Equal(expected, DouyinFileNameHelper.KeepChineseLettersAndNumbers(input));

        [Theory]
        [InlineData("name123", "name123")]
        [InlineData("clip(2)", "clip(2)")]
        public void RemoveNumberSuffix_locks(string input, string expected)
            => Assert.Equal(expected, DouyinFileNameHelper.RemoveNumberSuffix(input));

        [Theory]
        [InlineData("一二三四五六七八九十", 5, false, "一二三四五")]
        [InlineData("hello world long text", 8, true, "hello wo...")]
        public void LimitUnifiedCount_locks(string input, int max, bool ell, string expected)
            => Assert.Equal(expected, input.LimitUnifiedCount(max, ell));
    }
}
