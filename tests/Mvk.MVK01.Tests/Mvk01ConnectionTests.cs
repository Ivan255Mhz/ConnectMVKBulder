using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01ConnectionTests
    {
        [Fact]
        public async Task ConnectAsync_Default_DisallowsConfigurationWrite()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            Assert.False(connection.Device.Settings.AllowConfigurationWrite);
            Assert.True(connection.Device.Settings.RequireEvenAddressAndQuantity);
        }

        [Fact]
        public async Task ConnectAsync_AllowConfigurationWrite_EnablesConfigurationWrite()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port,
                allowConfigurationWrite: true);

            Assert.True(connection.Device.Settings.AllowConfigurationWrite);
        }

        [Fact]
        public async Task WriteProtectedArea_Default_ThrowsValidation()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
                () => connection.Device.Device.WriteMultipleRegistersAsync(0x0100, [0x0001, 0x0002]));

            Assert.Equal(MvkErrorKind.Validation, exception.ErrorKind);
        }

        [Fact]
        public async Task WriteProtectedArea_WithWriteAccess_Succeeds()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port,
                allowConfigurationWrite: true);

            await connection.Device.Device.WriteMultipleRegistersAsync(0x0100, [0x0001, 0x0002]);

            Assert.Equal(new ushort[] { 0x0001, 0x0002 }, server.GetHoldingRegisters(0x0100, 2));
        }

        [Fact]
        public async Task SaveConfigurationAsync_Default_ReturnsValidationError()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            Mvk01CommandResult result = await connection.SaveConfigurationAsync();

            Assert.Equal(Mvk01CommandStatus.TransportError, result.Status);
            Assert.NotNull(result.Error);
            Assert.Equal(MvkErrorKind.Validation, result.Error!.ErrorKind);
        }
    }
}
