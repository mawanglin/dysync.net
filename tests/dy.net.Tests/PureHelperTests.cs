using dy.net.model.dto;
using dy.net.utils;

namespace dy.net.Tests
{
    public class PureHelperTests
    {
        [Fact]
        public void Md5_is_stable()
        {
            // Well-known MD5 of "abc"; if this fails the helper is non-standard.
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", "abc".Md5());
        }

        [Fact]
        public void VideoTitleGenerator_Generate_locks_current_behavior()
        {
            var data = new VideoTitleDataTemplate
            {
                Id = "1001",
                VideoTitle = "测试标题!!abc",
                Author = "作者A@@123",
                FileHash = "deadbeef",
                Resolution = "1920x1080",
                ReleaseTime = new System.DateTime(2026, 5, 18)
            };
            var actual = VideoTitleGenerator.Generate(
                "{Id}-{VideoTitle}-{Author}-{Resolution}-{ReleaseTime}-{Unknown}",
                data);
            Assert.Equal("1001-测试标题abc-作者A123-1920x1080-20260518-{Unknown}", actual);
        }

        [Fact]
        public void VideoTitleGenerator_Generate_empty_fields_use_placeholder()
        {
            var data = new VideoTitleDataTemplate { Id = "7", VideoTitle = "!!!" };
            var actual = VideoTitleGenerator.Generate("{Id}_{VideoTitle}_{Author}", data);
            Assert.Equal("7__", actual);
        }
    }
}
