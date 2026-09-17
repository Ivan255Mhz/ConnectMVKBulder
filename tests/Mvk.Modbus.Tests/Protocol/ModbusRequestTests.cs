using System;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Protocol
{
    public sealed class ModbusRequestTests
    {
        [Fact]
        public void ReadHoldingRegisters_ValidArguments_CreatesRequest()
        {
            ModbusRequest request = ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2000, 43);

            Assert.Equal("mvk-1", request.DeviceId);
            Assert.Equal(ModbusOperation.ReadHoldingRegisters, request.Operation);
            Assert.Equal(0x2000, request.StartAddress);
            Assert.Equal(43, request.Quantity);
            Assert.Null(request.WriteData);
            Assert.False(request.IsWrite);
            Assert.Equal(0x2000 + 42, request.EndAddress);
        }

        [Theory]
        [InlineData((ushort)0)]
        [InlineData((ushort)126)]
        public void ReadHoldingRegisters_InvalidQuantity_Throws(ushort quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2000, quantity));
        }

        [Fact]
        public void ReadHoldingRegisters_AddressOverflow_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ModbusRequest.ReadHoldingRegisters("mvk-1", 0xFFFF, 2));
        }

        [Fact]
        public void WriteSingleRegister_CreatesRequestWithSingleValue()
        {
            ModbusRequest request = ModbusRequest.WriteSingleRegister("mvk-1", 0x0002, 1);

            Assert.True(request.IsWrite);
            Assert.Equal(1, request.Quantity);
            Assert.NotNull(request.WriteData);
            Assert.Equal(1, request.WriteData![0]);
        }

        [Fact]
        public void WriteMultipleRegisters_CreatesRequestWithData()
        {
            ushort[] data = [0x1234, 0x5678, 0x9ABC];
            ModbusRequest request = ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, data);

            Assert.True(request.IsWrite);
            Assert.Equal(3, request.Quantity);
            Assert.Same(data, request.WriteData);
        }

        [Fact]
        public void WriteMultipleRegisters_TooManyRegisters_Throws()
        {
            ushort[] data = new ushort[ModbusRequest.MaxWriteRegisters + 1];

            Assert.Throws<ArgumentException>(() => ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, data));
        }

        [Fact]
        public void WriteMultipleRegisters_EmptyData_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, []));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void AnyFactory_EmptyDeviceId_Throws(string deviceId)
        {
            Assert.Throws<ArgumentException>(() => ModbusRequest.ReadHoldingRegisters(deviceId, 0, 1));
        }

        [Fact]
        public void DeviceId_TrimsWhitespace()
        {
            ModbusRequest request = ModbusRequest.ReadHoldingRegisters("  mvk-1  ", 0, 1);

            Assert.Equal("mvk-1", request.DeviceId);
        }
    }
}
