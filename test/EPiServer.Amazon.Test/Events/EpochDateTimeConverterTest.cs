using System;
using Xunit;

namespace EPiServer.Amazon.Events
{
    public class EpochDateTimeConverterTest
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("3XX")]
        public void TryParse_WithInvalidString_ShouldReturnFalse(string s)
        {
            Assert.False(EpochDateTimeConverter.TryParse(s, out _));
        }

        [Fact]
        public void TryParse_WhenStringIsZero_ShouldReturnTrueAndEpochTime()
        {
            var result = EpochDateTimeConverter.TryParse("0", out var parsed);

            Assert.True(result);
            Assert.Equal(EpochDateTimeConverter.Epoch, parsed);
        }

        [Fact]
        public void TryParse_WhenStringIsValidNumber_ShouldReturnTrueAndCorrectTime()
        {
            double milliseconds = 1238099229000;
            var expected = EpochDateTimeConverter.Epoch.AddMilliseconds(milliseconds);
            var result = EpochDateTimeConverter.TryParse(milliseconds.ToString(), out var parsed);

            Assert.True(result);
            Assert.Equal(expected, parsed);
        }

        [Fact]
        public void TryParse_WhenStringIsValidNumberPaddedWithWhitespace_ShouldReturnTrueAndCorrectTime()
        {
            double milliseconds = 1250700979248;
            var expected = EpochDateTimeConverter.Epoch.AddMilliseconds(milliseconds);
            var result = EpochDateTimeConverter.TryParse(" " + milliseconds.ToString() + Environment.NewLine, out var parsed);

            Assert.True(result);
            Assert.Equal(expected, parsed);
        }

        [Fact]
        public void TryParse_ShouldParseToUtcTime()
        {
            var result = EpochDateTimeConverter.TryParse("1238099229000", out var parsed);

            Assert.True(result);
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
        }
    }
}
