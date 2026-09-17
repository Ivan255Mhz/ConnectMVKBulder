using System;
using System.Text;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Protocol
{
    public sealed class RegisterCodecTests
    {
        [Theory]
        [InlineData(MvkByteOrder.BigEndian, 0x3F80, 0x0000)]
        [InlineData(MvkByteOrder.Mixed, 0x0000, 0x3F80)]
        [InlineData(MvkByteOrder.LittleEndian, 0x0000, 0x803F)]
        public void ToSingle_OneFloat_AllByteOrders(MvkByteOrder byteOrder, ushort first, ushort second)
        {
            float value = RegisterCodec.ToSingle([first, second], byteOrder);

            Assert.Equal(1.0f, value);
        }

        [Theory]
        [InlineData(MvkByteOrder.BigEndian, 0x4145, 0x70A4)]
        [InlineData(MvkByteOrder.Mixed, 0x70A4, 0x4145)]
        [InlineData(MvkByteOrder.LittleEndian, 0xA470, 0x4541)]
        public void ToSingle_PreciseFloat_AllByteOrders(MvkByteOrder byteOrder, ushort first, ushort second)
        {
            float value = RegisterCodec.ToSingle([first, second], byteOrder);

            Assert.Equal(12.34f, value, 5);
        }

        [Theory]
        [InlineData(MvkByteOrder.BigEndian)]
        [InlineData(MvkByteOrder.Mixed)]
        [InlineData(MvkByteOrder.LittleEndian)]
        public void FromSingle_ToSingle_Roundtrip(MvkByteOrder byteOrder)
        {
            foreach (float expected in new[] { 0.0f, 1.0f, -0.5f, 12.34f, 65535.125f })
            {
                ushort[] registers = RegisterCodec.FromSingle(expected, byteOrder);

                Assert.Equal(2, registers.Length);
                Assert.Equal(expected, RegisterCodec.ToSingle(registers, byteOrder));
            }
        }

        [Theory]
        [InlineData(MvkByteOrder.BigEndian, 0x000F, 0x0001)]
        [InlineData(MvkByteOrder.Mixed, 0x0001, 0x000F)]
        [InlineData(MvkByteOrder.LittleEndian, 0x0100, 0x0F00)]
        public void ToUInt32_CommandValue_AllByteOrders(MvkByteOrder byteOrder, ushort first, ushort second)
        {
            uint value = RegisterCodec.ToUInt32([first, second], byteOrder);

            Assert.Equal(0x000F0001u, value);
        }

        [Theory]
        [InlineData(MvkByteOrder.BigEndian)]
        [InlineData(MvkByteOrder.Mixed)]
        [InlineData(MvkByteOrder.LittleEndian)]
        public void FromUInt32_ToUInt32_Roundtrip(MvkByteOrder byteOrder)
        {
            ushort[] registers = RegisterCodec.FromUInt32(0x00BADu, byteOrder);

            Assert.Equal(0x00BADu, RegisterCodec.ToUInt32(registers, byteOrder));
        }

        [Fact]
        public void ToUInt32_SingleRegister_Throws()
        {
            Assert.Throws<ArgumentException>(() => RegisterCodec.ToUInt32([0x1234], MvkByteOrder.BigEndian));
        }

        [Fact]
        public void ToInt32_NegativeValue_ReturnsSigned()
        {
            ushort[] registers = RegisterCodec.FromUInt32(0xFFFFFFFEu, MvkByteOrder.BigEndian);

            Assert.Equal(-2, RegisterCodec.ToInt32(registers, MvkByteOrder.BigEndian));
        }

        [Fact]
        public void Text_Roundtrip_AsciiWithPadding()
        {
            ushort[] registers = RegisterCodec.FromText("m/s2", 16, Encoding.ASCII);

            string text = RegisterCodec.ToText(registers, Encoding.ASCII);

            Assert.Equal("m/s2", text);
        }

        [Fact]
        public void ToText_StopsAtZeroByte()
        {
            ushort[] registers = [('V' << 8) | 'A', ('S' << 8) | 'T', 0, ('X' << 8) | 'Y'];

            string text = RegisterCodec.ToText(registers, Encoding.ASCII);

            Assert.Equal("VAST", text);
        }

        [Fact]
        public void FromText_TooLong_Throws()
        {
            Assert.Throws<ArgumentException>(() => RegisterCodec.FromText("слишком длинная строка", 2, Encoding.ASCII));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(6, true)]
        [InlineData(15, false)]
        public void GetBit_UInt16_ReturnsExpected(int bitIndex, bool expected)
        {
            ushort value = 0b0100_0001;

            Assert.Equal(expected, RegisterCodec.GetBit(value, bitIndex));
        }

        [Theory]
        [InlineData(9)]
        [InlineData(31)]
        public void GetBit_UInt32_ReturnsExpected(int bitIndex)
        {
            uint value = 1u << bitIndex;

            Assert.True(RegisterCodec.GetBit(value, bitIndex));
        }

        [Fact]
        public void GetBit_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RegisterCodec.GetBit((ushort)1, 16));
            Assert.Throws<ArgumentOutOfRangeException>(() => RegisterCodec.GetBit(1u, -1));
        }
    }
}
