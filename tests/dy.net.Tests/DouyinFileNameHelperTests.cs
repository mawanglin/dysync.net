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
