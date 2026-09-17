using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01RegisterMapTests
    {
        [Theory]
        [InlineData(1, 0x0100)]
        [InlineData(2, 0x0200)]
        [InlineData(4, 0x0400)]
        [InlineData(8, 0x0800)]
        public void ChannelConfig_ReturnsExpectedBase(int channel, int expected)
        {
            Assert.Equal(expected, Mvk01RegisterMap.ChannelConfig(channel));
        }

        [Theory]
        [InlineData(1, 0x2000)]
        [InlineData(2, 0x2200)]
        [InlineData(8, 0x2E00)]
        public void ChannelData_ReturnsExpectedBase(int channel, int expected)
        {
            Assert.Equal(expected, Mvk01RegisterMap.ChannelData(channel));
        }

        [Theory]
        [InlineData(1, 0x3000)]
        [InlineData(2, 0x3100)]
        public void TachoData_ReturnsExpectedBase(int tacho, int expected)
        {
            Assert.Equal(expected, Mvk01RegisterMap.TachoData(tacho));
        }

        [Theory]
        [InlineData(1, 0x0900)]
        [InlineData(2, 0x0A00)]
        public void TachoConfig_ReturnsExpectedBase(int tacho, int expected)
        {
            Assert.Equal(expected, Mvk01RegisterMap.TachoConfig(tacho));
        }

        // Примечание: в MVK01_registers.txt пример для N=48 (0x41F0) противоречит формуле
        // 0x4000+(N-1)x16; расчёт по формуле даёт 0x42F0 (проверено на реальном устройстве).
        [Theory]
        [InlineData(1, 0x4000)]
        [InlineData(32, 0x41F0)]
        [InlineData(48, 0x42F0)]
        [InlineData(184, 0x4B70)]
        public void MeasurementByNumber_ReturnsExpectedAddress(int number, int expected)
        {
            Assert.Equal(expected, Mvk01RegisterMap.MeasurementByNumber(number));
        }

        [Fact]
        public void SpectrumRegisterCount_MatchesBands()
        {
            Assert.Equal(Mvk01RegisterMap.SpectrumBands * 2, Mvk01RegisterMap.SpectrumRegisterCount);
        }
    }
}
