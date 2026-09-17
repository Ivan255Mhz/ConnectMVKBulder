using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Pipeline
{
    public sealed class MvkPipelineTests
    {
        [Fact]
        public async Task ExecuteAsync_MiddlewaresRunInOrder()
        {
            List<string> calls = [];

            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use((context, next, ct) => LogAndNext(calls, "первый", next, context, ct))
                .Use((context, next, ct) => LogAndNext(calls, "второй", next, context, ct))
                .Build((context, ct) =>
                {
                    calls.Add("терминал");
                    context.Response = ModbusResponse.Success([1]);
                    return Task.CompletedTask;
                });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext());

            Assert.True(response.IsSuccess);
            Assert.Equal(new[] { "первый", "второй", "терминал" }, calls);
        }

        [Fact]
        public async Task ExecuteAsync_ShortCircuit_SkipsRestOfPipeline()
        {
            bool terminalCalled = false;

            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use((context, next, ct) =>
                {
                    context.Response = ModbusResponse.Failure(
                        new MvkModbusException("запрещено", MvkErrorKind.Validation, context.Settings.Id));
                    return Task.CompletedTask;
                })
                .Build((context, ct) =>
                {
                    terminalCalled = true;
                    context.Response = ModbusResponse.Success([]);
                    return Task.CompletedTask;
                });

            ModbusResponse response = await pipeline.ExecuteAsync(CreateContext());

            Assert.False(response.IsSuccess);
            Assert.False(terminalCalled);
        }

        [Fact]
        public async Task ExecuteAsync_CancellationTokenPassedToTerminal()
        {
            using CancellationTokenSource cts = new();
            CancellationToken observed = default;

            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Build((context, ct) =>
                {
                    observed = ct;
                    context.Response = ModbusResponse.Success([]);
                    return Task.CompletedTask;
                });

            await pipeline.ExecuteAsync(CreateContext(cts.Token));

            Assert.Equal(cts.Token, observed);
        }

        [Fact]
        public async Task ExecuteAsync_MiddlewareCanReplaceTokenForDownstream()
        {
            using CancellationTokenSource linked = new();
            CancellationToken observed = default;

            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Use((context, next, ct) => next(context, linked.Token))
                .Build((context, ct) =>
                {
                    observed = ct;
                    context.Response = ModbusResponse.Success([]);
                    return Task.CompletedTask;
                });

            await pipeline.ExecuteAsync(CreateContext());

            Assert.Equal(linked.Token, observed);
        }

        [Fact]
        public async Task ExecuteAsync_TerminalNotCalled_Throws()
        {
            MvkPipeline pipeline = new MvkPipelineBuilder()
                .Build((context, ct) => Task.CompletedTask);

            MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
                () => pipeline.ExecuteAsync(CreateContext()));

            Assert.Equal(MvkErrorKind.Unknown, exception.ErrorKind);
        }

        [Fact]
        public void MiddlewareInstance_Null_Throws()
        {
            MvkPipelineBuilder builder = new();

            Assert.Throws<ArgumentNullException>(() => builder.Use((IModbusMiddleware)null!));
            Assert.Throws<ArgumentNullException>(() => builder.Use((Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task>)null!));
            Assert.Throws<ArgumentNullException>(() => builder.Build(null!));
        }

        private static Task LogAndNext(
            List<string> calls,
            string name,
            InvokeModbusDelegate next,
            ModbusRequestContext context,
            CancellationToken cancellationToken)
        {
            calls.Add(name);
            return next(context, cancellationToken);
        }

        private static ModbusRequestContext CreateContext(CancellationToken cancellationToken = default)
        {
            return new ModbusRequestContext(
                TestDeviceSettings.Create(),
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2),
                cancellationToken);
        }
    }
}
