using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.MVK01.Polling;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests.Polling
{
    public sealed class Mvk01WatcherBatchingTests
    {
        [Fact]
        public async Task WatchAsync_SevenAdjacentItems_UsesOneRequest()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));
                Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));
                Mvk01PollItem[] items = CreateItems(49, 7);

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                List<Mvk01Sample> samples = await TakeAsync(watcher, items, count: 7, cts.Token);

                Assert.Equal(7, samples.Count);
                Assert.All(samples, sample => Assert.Equal(samples[0].Timestamp, sample.Timestamp));

                ModbusRequest request = Assert.Single(transport.Requests);
                Assert.Equal(Mvk01RegisterMap.MeasurementByNumber(49), request.StartAddress);
                Assert.Equal(112, request.Quantity);
            }
        }

        [Fact]
        public async Task WatchAsync_EightAdjacentItems_UsesTwoRequests()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));
                Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));
                Mvk01PollItem[] items = CreateItems(49, 8);

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                List<Mvk01Sample> samples = await TakeAsync(watcher, items, count: 8, cts.Token);

                Assert.Equal(8, samples.Count);
                Assert.Equal(2, transport.Requests.Count);
                Assert.Equal(Mvk01RegisterMap.MeasurementByNumber(49), transport.Requests[0].StartAddress);
                Assert.Equal(112, transport.Requests[0].Quantity);
                Assert.Equal(Mvk01RegisterMap.MeasurementByNumber(56), transport.Requests[1].StartAddress);
                Assert.Equal(16, transport.Requests[1].Quantity);
            }
        }

        [Fact]
        public async Task WatchAsync_CustomBatchLimit_RespectsIt()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);
            Mvk01SnapshotOptions options = new() { MaxMeasurementBatchBlocks = 3 };

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));
                Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5), options);
                Mvk01PollItem[] items = CreateItems(49, 7);

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                await TakeAsync(watcher, items, count: 7, cts.Token);

                Assert.Equal(3, transport.Requests.Count);
                Assert.Equal(48, transport.Requests[0].Quantity);
                Assert.Equal(48, transport.Requests[1].Quantity);
                Assert.Equal(16, transport.Requests[2].Quantity);
            }
        }

        [Fact]
        public async Task WatchAsync_AdjacentParameters_DecodeCorrectValues()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedFloats(server, Mvk01RegisterMap.MeasurementByNumber(49), 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f);
            SeedFloats(server, Mvk01RegisterMap.MeasurementByNumber(50), 11f, 12f, 13f, 14f, 15f, 16f, 17f, 18f);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));
            Mvk01PollItem[] items = CreateItems(49, 2);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            List<Mvk01Sample> samples = await TakeAsync(watcher, items, count: 2, cts.Token);

            Assert.Equal(1f, samples[0].Values[0]);
            Assert.Equal(11f, samples[1].Values[0]);
        }

        [Fact]
        public async Task WatchAsync_BatchError_ReturnsErrorForEachItem()
        {
            (MvkDevicePool pool, _) = CreateFakePool(request =>
                request.Quantity == 112
                    ? ModbusResponse.Failure(new MvkModbusException(
                        "ошибка устройства",
                        MvkErrorKind.DeviceError,
                        "mvk-1"))
                    : ZeroHandler(request));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));
                Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));
                Mvk01PollItem[] items = CreateItems(49, 7);

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                List<Mvk01Sample> samples = await TakeAsync(watcher, items, count: 7, cts.Token);

                Assert.Equal(7, samples.Count);
                Assert.All(samples, sample =>
                {
                    Assert.False(sample.IsSuccess);
                    Assert.Equal(MvkErrorKind.DeviceError, sample.Error!.ErrorKind);
                    Assert.Empty(sample.Values);
                });
            }
        }

        private static async Task<List<Mvk01Sample>> TakeAsync(
            Mvk01Watcher watcher,
            IReadOnlyList<Mvk01PollItem> items,
            int count,
            CancellationToken cancellationToken)
        {
            List<Mvk01Sample> samples = [];

            await foreach (Mvk01Sample sample in watcher.WatchAsync(items, cancellationToken))
            {
                samples.Add(sample);

                if (samples.Count == count)
                {
                    break;
                }
            }

            return samples;
        }

        private static Mvk01PollItem[] CreateItems(int startNumber, int count)
        {
            return Enumerable
                .Range(startNumber, count)
                .Select(number => new Mvk01PollItem((Mvk01MeasurementNumber)number))
                .ToArray();
        }

        private static ModbusResponse ZeroHandler(ModbusRequest request)
        {
            return request.IsWrite
                ? ModbusResponse.WriteSuccess()
                : ModbusResponse.Success(new ushort[request.Quantity]);
        }

        private static void SeedFloats(MvkTestServer server, ushort address, params float[] values)
        {
            ushort[] registers = new ushort[values.Length * 2];

            for (int index = 0; index < values.Length; index++)
            {
                RegisterCodec
                    .FromSingle(values[index], MvkByteOrder.BigEndian)
                    .CopyTo(registers, index * 2);
            }

            server.SetHoldingRegisters(address, registers);
        }

        private static MvkDevicePool CreatePool(int port)
        {
            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions
                {
                    Port = port,
                    RequireEvenAddressAndQuantity = true,
                    AllowConfigurationWrite = true
                },
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return new MvkDevicePool(options);
        }

        private static (MvkDevicePool Pool, FakeModbusTransport Transport) CreateFakePool(
            Func<ModbusRequest, ModbusResponse> handler)
        {
            FakeModbusTransport transport = new(handler);
            FakeModbusTransportFactory factory = new(_ => transport);

            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions
                {
                    RequireEvenAddressAndQuantity = true,
                    AllowConfigurationWrite = true
                },
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return (new MvkDevicePool(options, factory, middlewares: []), transport);
        }
    }
}
