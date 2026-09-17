using System;
using System.IO;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Protocol
{
    public sealed class ModbusResponseTests
    {
        [Fact]
        public void Success_CarriesData()
        {
            ushort[] data = [1, 2, 3];
            ModbusResponse response = ModbusResponse.Success(data);

            Assert.True(response.IsSuccess);
            Assert.Same(data, response.Data);
            Assert.Null(response.Error);
        }

        [Fact]
        public void WriteSuccess_HasNoData()
        {
            ModbusResponse response = ModbusResponse.WriteSuccess();

            Assert.True(response.IsSuccess);
            Assert.Empty(response.Data);
            Assert.Null(response.Error);
        }

        [Fact]
        public void Failure_CarriesError()
        {
            MvkModbusException error = new("нет связи", MvkErrorKind.Transport, "mvk-1");
            ModbusResponse response = ModbusResponse.Failure(error);

            Assert.False(response.IsSuccess);
            Assert.Empty(response.Data);
            Assert.Same(error, response.Error);
        }

        [Fact]
        public void Failure_FromGenericException_WrapsAsTransport()
        {
            IOException exception = new("соединение сброшено");
            ModbusResponse response = ModbusResponse.Failure(exception, "mvk-1");

            Assert.False(response.IsSuccess);
            Assert.NotNull(response.Error);
            Assert.Equal(MvkErrorKind.Transport, response.Error!.ErrorKind);
            Assert.Equal("mvk-1", response.Error.DeviceId);
            Assert.Same(exception, response.Error.InnerException);
        }

        [Fact]
        public void Failure_FromMvkException_PreservesInstance()
        {
            MvkModbusException error = new("таймаут", MvkErrorKind.Timeout, "mvk-1");
            ModbusResponse response = ModbusResponse.Failure(error);

            Assert.Same(error, response.Error);
        }
    }
}
