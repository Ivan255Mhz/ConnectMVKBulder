using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;
using Mvk.Testing;
using Xunit;

namespace Mvk.Modbus.Tests.Pipeline
{
    public sealed class PipelineIntegrationTests
    {
        [Fact]
        public async Task FullPipeline_ReadAgainstEmulator_Succeeds()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            server.SetHoldingRegisters(0x1004, 0x0100, 0x4301);

            MvkDeviceSettings settings = TestDeviceSettings.Create(
                port: server.Port,
                maxRetries: 1,
                retryDelayMs: 10,
                requireEvenAddressAndQuantity: true);
            using NModbusTransport transport = new(settings);

            int attempts = 0;
            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use(new LoggingMiddleware())
                .Use(new RetryMiddleware())
                .Use((context, next, ct) =>
                {
                    attempts++;
                    return next(context, ct);
                })
                .Use(new ValidationMiddleware())
                .Use(new WriteProtectionMiddleware())
                .Build(MvkPipeline.TerminalFromTransport(transport));

            ModbusRequestContext context = new(
                settings,
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1004, 2),
                CancellationToken.None);

            ModbusResponse response = await pipeline.ExecuteAsync(context);

            Assert.True(response.IsSuccess, response.Error?.Message);
            Assert.Equal(new ushort[] { 0x0100, 0x4301 }, response.Data);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task FullPipeline_ServerDown_RetriesThenFails()
        {
            int freePort = MvkTestServer.GetFreePort();
            MvkDeviceSettings settings = TestDeviceSettings.Create(
                port: freePort,
                connectTimeoutMs: 300,
                maxRetries: 2,
                retryDelayMs: 10);
            using NModbusTransport transport = new(settings);

            int attempts = 0;
            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use(new RetryMiddleware())
                .Use((context, next, ct) =>
                {
                    attempts++;
                    return next(context, ct);
                })
                .Build(MvkPipeline.TerminalFromTransport(transport));

            ModbusResponse response = await pipeline.ExecuteAsync(new ModbusRequestContext(
                settings,
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x1000, 2),
                CancellationToken.None));

            Assert.False(response.IsSuccess);
            Assert.True(response.Error!.IsTransient);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task FullPipeline_ValidationBlocksOddRequest_DeviceNotContacted()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            MvkDeviceSettings settings = TestDeviceSettings.Create(
                port: server.Port,
                requireEvenAddressAndQuantity: true);
            using NModbusTransport transport = new(settings);

            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use(new ValidationMiddleware())
                .Build(MvkPipeline.TerminalFromTransport(transport));

            ModbusResponse response = await pipeline.ExecuteAsync(new ModbusRequestContext(
                settings,
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 2),
                CancellationToken.None));

            Assert.False(response.IsSuccess);
            Assert.Equal(MvkErrorKind.Validation, response.Error!.ErrorKind);
            Assert.False(transport.IsConnected);
        }
    }
}
