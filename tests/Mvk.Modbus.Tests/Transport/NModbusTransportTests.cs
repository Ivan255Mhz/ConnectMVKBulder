using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;
using Mvk.Testing;
using Xunit;

namespace Mvk.Modbus.Tests.Transport
{
    public sealed class NModbusTransportTests
    {
        [Fact]
        public async Task ExecuteAsync_ReadHoldingRegisters_ReturnsData()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            server.SetHoldingRegisters(0x1004, 0x0107, 0x1400);

            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));

            ModbusResponse response = await transport.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2),
                CancellationToken.None);

            Assert.True(response.IsSuccess, response.Error?.Message);
            Assert.Equal(new ushort[] { 0x0107, 0x1400 }, response.Data);
        }

        [Fact]
        public async Task ExecuteAsync_WriteSingleRegister_UpdatesServer()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));

            ModbusResponse response = await transport.ExecuteAsync(
                ModbusRequest.WriteSingleRegister("mvk-1", 0x0002, 0x0001),
                CancellationToken.None);

            Assert.True(response.IsSuccess, response.Error?.Message);
            Assert.Equal(0x0001, server.GetHoldingRegister(0x0002));
        }

        [Fact]
        public async Task ExecuteAsync_WriteMultipleRegisters_UpdatesServer()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));

            ModbusResponse response = await transport.ExecuteAsync(
                ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, [0x1111, 0x2222, 0x3333]),
                CancellationToken.None);

            Assert.True(response.IsSuccess, response.Error?.Message);
            Assert.Equal(new ushort[] { 0x1111, 0x2222, 0x3333 }, server.GetHoldingRegisters(0x0100, 3));
        }

        [Fact]
        public async Task ExecuteAsync_ConnectsLazily()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));

            Assert.False(transport.IsConnected);

            await transport.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                CancellationToken.None);

            Assert.True(transport.IsConnected);
        }

        [Fact]
        public async Task ConnectAsync_Explicit_Connects()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));

            await transport.ConnectAsync(CancellationToken.None);

            Assert.True(transport.IsConnected);
        }

        [Fact]
        public async Task ExecuteAsync_ServerUnreachable_ReturnsTransientConnectionError()
        {
            int freePort = MvkTestServer.GetFreePort();
            using NModbusTransport transport = new(TestDeviceSettings.Create(port: freePort, connectTimeoutMs: 500));

            ModbusResponse response = await transport.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                CancellationToken.None);

            Assert.False(response.IsSuccess);
            Assert.NotNull(response.Error);
            Assert.True(response.Error!.IsTransient);
            Assert.Contains(response.Error.ErrorKind, new[] { MvkErrorKind.Transport, MvkErrorKind.Timeout });
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task ExecuteAsync_SilentServer_ReturnsTimeoutError()
        {
            await using SilentTcpServer silent = SilentTcpServer.Start();
            using NModbusTransport transport = new(TestDeviceSettings.Create(port: silent.Port, readTimeoutMs: 300));

            ModbusResponse response = await transport.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                CancellationToken.None);

            Assert.False(response.IsSuccess);
            Assert.NotNull(response.Error);
            Assert.Equal(MvkErrorKind.Timeout, response.Error!.ErrorKind);
            Assert.False(transport.IsConnected);
        }

        [Fact]
        public async Task ExecuteAsync_AfterServerRestart_Reconnects()
        {
            int port = MvkTestServer.GetFreePort();

            await using (MvkTestServer first = MvkTestServer.Start(port: port))
            {
                using NModbusTransport transport = new(TestDeviceSettings.Create(port: port));

                ModbusResponse response = await transport.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                    CancellationToken.None);
                Assert.True(response.IsSuccess, response.Error?.Message);

                await first.DisposeAsync();

                ModbusResponse failed = await transport.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                    CancellationToken.None);
                Assert.False(failed.IsSuccess);
                Assert.False(transport.IsConnected);

                await using MvkTestServer second = MvkTestServer.Start(port: port);
                second.SetHoldingRegister(0x0000, 0xABCD);

                ModbusResponse recovered = await transport.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                    CancellationToken.None);

                Assert.True(recovered.IsSuccess, recovered.Error?.Message);
                Assert.Equal(0xABCD, recovered.Data[0]);
                Assert.True(transport.IsConnected);
            }
        }

        [Fact]
        public async Task ExecuteAsync_CancelledToken_ThrowsOperationCanceled()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            using NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));
            using CancellationTokenSource cts = new();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                    cts.Token));
        }

        [Fact]
        public async Task ExecuteAsync_AfterDispose_ThrowsObjectDisposed()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            NModbusTransport transport = new(TestDeviceSettings.Create(port: server.Port));
            transport.Dispose();

            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => transport.ExecuteAsync(
                    ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 1),
                    CancellationToken.None));
        }

        [Fact]
        public void MapException_Timeout_MapsToTimeoutKind()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(port: 502);

            Assert.Equal(MvkErrorKind.Timeout, NModbusTransport.MapException(settings, new TimeoutException()).ErrorKind);
            Assert.Equal(MvkErrorKind.Timeout, NModbusTransport.MapException(settings, new IOException("Read timed out")).ErrorKind);
            Assert.Equal(MvkErrorKind.Timeout, NModbusTransport.MapException(settings, new IOException("wrapped", new TimeoutException())).ErrorKind);
        }

        [Fact]
        public void MapException_SocketErrors_MapsByKind()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(port: 502);

            Assert.Equal(
                MvkErrorKind.Timeout,
                NModbusTransport.MapException(settings, new SocketException((int)SocketError.TimedOut)).ErrorKind);

            Assert.Equal(
                MvkErrorKind.Transport,
                NModbusTransport.MapException(settings, new SocketException((int)SocketError.ConnectionReset)).ErrorKind);
        }

        [Fact]
        public void MapException_UnknownError_MapsToTransportWithDeviceId()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(port: 502, id: "mvk-7");

            MvkModbusException error = NModbusTransport.MapException(settings, new InvalidOperationException("сбой"));

            Assert.Equal(MvkErrorKind.Transport, error.ErrorKind);
            Assert.Equal("mvk-7", error.DeviceId);
            Assert.True(error.IsTransient);
        }
    }
}
