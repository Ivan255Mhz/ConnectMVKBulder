using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Pipeline
{
    public sealed class MiddlewareTests
    {
        [Fact]
        public async Task Retry_TransientFailure_RetriesUntilSuccess()
        {
            int attempts = 0;
            MvkDeviceSettings settings = TestDeviceSettings.Create(maxRetries: 3);
            MvkPipeline pipeline = BuildSingle(new RetryMiddleware(), settings, (context, ct) =>
            {
                attempts++;
                context.Response = attempts < 3
                    ? ModbusResponse.Failure(new MvkModbusException("нет связи", MvkErrorKind.Transport, "mvk-1"))
                    : ModbusResponse.Success([0x1234]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.True(response.IsSuccess);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task Retry_NonTransientFailure_NotRetried()
        {
            int attempts = 0;
            MvkDeviceSettings settings = TestDeviceSettings.Create(maxRetries: 3);
            MvkPipeline pipeline = BuildSingle(new RetryMiddleware(), settings, (context, ct) =>
            {
                attempts++;
                context.Response = ModbusResponse.Failure(
                    new MvkModbusException("неверный адрес", MvkErrorKind.DeviceError, "mvk-1", modbusExceptionCode: 0x02));
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.False(response.IsSuccess);
            Assert.Equal(1, attempts);
        }

        [Fact]
        public async Task Retry_Exhausted_ReturnsLastError()
        {
            int attempts = 0;
            MvkDeviceSettings settings = TestDeviceSettings.Create(maxRetries: 2, retryDelayMs: 1);
            MvkPipeline pipeline = BuildSingle(new RetryMiddleware(), settings, (context, ct) =>
            {
                attempts++;
                context.Response = ModbusResponse.Failure(
                    new MvkModbusException("таймаут", MvkErrorKind.Timeout, "mvk-1"));
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.False(response.IsSuccess);
            Assert.Equal(MvkErrorKind.Timeout, response.Error!.ErrorKind);
            Assert.Equal(3, attempts);
        }

        [Fact]
        public async Task Timeout_Exceeded_ReturnsTimeoutError()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new TimeoutMiddleware(), settings, async (context, ct) =>
            {
                await Task.Delay(2000, ct);
                context.Response = ModbusResponse.Success([]);
            });

            ModbusRequestContext context = CreateContext(settings);
            context.Items[MvkContextItems.TimeoutMs] = 100;

            ModbusResponse response = await pipeline.ExecuteAsync(context);

            Assert.False(response.IsSuccess);
            Assert.Equal(MvkErrorKind.Timeout, response.Error!.ErrorKind);
        }

        [Fact]
        public async Task Timeout_NotConfigured_PassesThrough()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new TimeoutMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.True(response.IsSuccess);
        }

        [Fact]
        public async Task Timeout_ExternalCancellation_Propagates()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new TimeoutMiddleware(), settings, async (context, ct) =>
            {
                await Task.Delay(2000, ct);
                context.Response = ModbusResponse.Success([]);
            });

            using CancellationTokenSource cts = new();
            cts.CancelAfter(50);

            ModbusRequestContext context = new(
                settings,
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
                cts.Token);
            context.Items[MvkContextItems.TimeoutMs] = 5000;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteAsync(context));
        }

        [Theory]
        [InlineData(0x2001, 2)]
        [InlineData(0x2002, 1)]
        [InlineData(0x2003, 3)]
        public async Task Validation_OddAddressOrQuantity_Fails(ushort address, ushort quantity)
        {
            bool terminalCalled = false;
            MvkDeviceSettings settings = TestDeviceSettings.Create(requireEvenAddressAndQuantity: true);
            MvkPipeline pipeline = BuildSingle(new ValidationMiddleware(), settings, (context, ct) =>
            {
                terminalCalled = true;
                context.Response = ModbusResponse.Success([]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.ReadHoldingRegisters("mvk-1", address, quantity)));

            Assert.False(response.IsSuccess);
            Assert.Equal(MvkErrorKind.Validation, response.Error!.ErrorKind);
            Assert.False(terminalCalled);
        }

        [Fact]
        public async Task Validation_EvenAddressAndQuantity_Passes()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(requireEvenAddressAndQuantity: true);
            MvkPipeline pipeline = BuildSingle(new ValidationMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1, 2]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2002, 86)));

            Assert.True(response.IsSuccess);
        }

        [Fact]
        public async Task Validation_AlignmentNotRequired_OddAddressPasses()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new ValidationMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.ReadHoldingRegisters("mvk-1", 0x2001, 1)));

            Assert.True(response.IsSuccess);
        }

        [Fact]
        public async Task WriteProtection_ReadOnly_DeniesWriteButAllowsRead()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(writePolicy: MvkWritePolicy.ReadOnly);

            MvkPipeline writePipeline = BuildSingle(new WriteProtectionMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.WriteSuccess();
                return Task.CompletedTask;
            });

            ModbusResponse denied = await writePipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.WriteSingleRegister("mvk-1", 0x0002, 1)));

            Assert.False(denied.IsSuccess);
            Assert.Equal(MvkErrorKind.Validation, denied.Error!.ErrorKind);

            MvkPipeline readPipeline = BuildSingle(new WriteProtectionMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1]);
                return Task.CompletedTask;
            });

            ModbusResponse allowed = await readPipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2)));

            Assert.True(allowed.IsSuccess);
        }

        [Fact]
        public async Task WriteProtection_ProtectConfigArea_DeniesProtectedRange()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(
                writePolicy: MvkWritePolicy.ProtectConfigArea,
                protectedStartAddress: 0x0000,
                protectedEndAddress: 0x0FFF);
            MvkPipeline pipeline = BuildSingle(new WriteProtectionMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.WriteSuccess();
                return Task.CompletedTask;
            });

            ModbusResponse denied = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.WriteMultipleRegisters("mvk-1", 0x0100, [1, 2])));

            Assert.False(denied.IsSuccess);
            Assert.Equal(MvkErrorKind.Validation, denied.Error!.ErrorKind);
        }

        [Fact]
        public async Task WriteProtection_ProtectConfigArea_AllowsOutsideProtectedRange()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(
                writePolicy: MvkWritePolicy.ProtectConfigArea,
                protectedStartAddress: 0x0000,
                protectedEndAddress: 0x0FFF);
            MvkPipeline pipeline = BuildSingle(new WriteProtectionMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.WriteSuccess();
                return Task.CompletedTask;
            });

            ModbusResponse allowed = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.WriteSingleRegister("mvk-1", 0x2000, 0x1234)));

            Assert.True(allowed.IsSuccess);
        }

        [Fact]
        public async Task WriteProtection_AllowConfigurationWrite_PermitsProtectedRange()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create(
                writePolicy: MvkWritePolicy.ProtectConfigArea,
                allowConfigurationWrite: true,
                protectedStartAddress: 0x0000,
                protectedEndAddress: 0x0FFF);
            MvkPipeline pipeline = BuildSingle(new WriteProtectionMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.WriteSuccess();
                return Task.CompletedTask;
            });

            ModbusResponse allowed = await pipeline.ExecuteAsync(
                CreateContext(settings, ModbusRequest.WriteSingleRegister("mvk-1", 0x0002, 1)));

            Assert.True(allowed.IsSuccess);
        }

        [Fact]
        public async Task Logging_PreservesResponseAndWritesLog()
        {
            RecordingLogger<LoggingMiddleware> logger = new();
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new LoggingMiddleware(logger), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.True(response.IsSuccess);
            Assert.Single(logger.Messages);
            Assert.Contains("mvk-1", logger.Messages[0]);
        }

        [Fact]
        public async Task Metrics_PreservesResponse()
        {
            MvkDeviceSettings settings = TestDeviceSettings.Create();
            MvkPipeline pipeline = BuildSingle(new MetricsMiddleware(), settings, (context, ct) =>
            {
                context.Response = ModbusResponse.Success([1]);
                return Task.CompletedTask;
            });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext(settings));

            Assert.True(response.IsSuccess);
        }

        private static MvkPipeline BuildSingle(
            IModbusMiddleware middleware,
            MvkDeviceSettings settings,
            InvokeModbusDelegate terminal)
        {
            return new MvkPipelineBuilder()
                .Use(middleware)
                .Build((context, ct) => terminal(context, ct));
        }

        private static ModbusRequestContext CreateContext(MvkDeviceSettings settings, ModbusRequest? request = null)
        {
            return new ModbusRequestContext(
                settings,
                request ?? ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
                CancellationToken.None);
        }

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
