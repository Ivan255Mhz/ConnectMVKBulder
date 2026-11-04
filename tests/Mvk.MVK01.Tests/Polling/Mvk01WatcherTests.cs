using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public sealed class Mvk01WatcherTests
    {
        [Fact]
        public async Task WatchAsync_FirstSampleIsImmediate()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));

            Mvk01Sample sample = await FirstAsync(
                watcher.WatchAsync(
                    new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration),
                    cts.Token));

            Assert.True(sample.IsSuccess);
        }

        [Fact]
        public async Task WatchAsync_ReturnsAllChannelsAndSelectedValue()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            float[] expected = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
            SeedFloats(server, Mvk01RegisterMap.MeasurementByNumber(55), expected);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));

            Mvk01Sample sample = await FirstAsync(
                watcher.WatchAsync(
                    new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 3),
                    cts.Token));

            Assert.True(sample.IsSuccess);
            Assert.Null(sample.Error);
            Assert.Equal(expected, sample.Values);
            Assert.Equal(3f, sample.ChannelValue!.Value);
        }

        [Fact]
        public async Task WatchAsync_MultipleItems_ReturnsOneSamplePerItem()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromSeconds(5));

            Mvk01PollItem[] items =
            [
                new(Mvk01MeasurementNumber.Rms10To1000Acceleration),
                new(Mvk01MeasurementNumber.RmsX25kHzAcceleration)
            ];

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(1));
            await using IAsyncEnumerator<Mvk01Sample> enumerator = watcher
                .WatchAsync(items, cts.Token)
                .GetAsyncEnumerator();

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(Mvk01MeasurementNumber.Rms10To1000Acceleration, enumerator.Current.Item.Number);

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(Mvk01MeasurementNumber.RmsX25kHzAcceleration, enumerator.Current.Item.Number);
        }

        [Fact]
        public async Task WatchAsync_ErrorSample_ContinuesPolling()
        {
            int calls = 0;
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(request =>
            {
                calls++;

                return calls == 1
                    ? ModbusResponse.Failure(
                        new MvkModbusException("ошибка устройства", MvkErrorKind.DeviceError, "mvk-1"))
                    : ModbusResponse.Success(EncodeFloats(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f));
            });

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));
                Mvk01Watcher watcher = new(device, TimeSpan.FromMilliseconds(10));

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                List<Mvk01Sample> samples = [];

                await foreach (Mvk01Sample sample in watcher.WatchAsync(
                    new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 1),
                    cts.Token))
                {
                    samples.Add(sample);

                    if (samples.Count == 2)
                    {
                        break;
                    }
                }

                Assert.Equal(2, samples.Count);
                Assert.False(samples[0].IsSuccess);
                Assert.Equal(MvkErrorKind.DeviceError, samples[0].Error!.ErrorKind);
                Assert.Empty(samples[0].Values);
                Assert.Null(samples[0].ChannelValue);
                Assert.True(samples[1].IsSuccess);
                Assert.Equal(8, samples[1].Values.Length);
                Assert.Equal(1f, samples[1].ChannelValue!.Value);
            }
        }

        [Fact]
        public async Task WatchAsync_CancellationAfterFirstSample_Throws()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromMilliseconds(10));

            using CancellationTokenSource cts = new();
            await using IAsyncEnumerator<Mvk01Sample> enumerator = watcher
                .WatchAsync(new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration), cts.Token)
                .GetAsyncEnumerator();

            Assert.True(await enumerator.MoveNextAsync());

            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await enumerator.MoveNextAsync();
            });
        }

        [Fact]
        public async Task WatchAsync_CanceledBeforeStart_Throws()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device);

            using CancellationTokenSource cts = new();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (Mvk01Sample _ in watcher.WatchAsync(
                    new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration),
                    cts.Token))
                {
                }
            });
        }

        [Fact]
        public async Task WatchAsync_WaitsBetweenCycles()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device, TimeSpan.FromMilliseconds(80));

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            await using IAsyncEnumerator<Mvk01Sample> enumerator = watcher
                .WatchAsync(new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration), cts.Token)
                .GetAsyncEnumerator();

            Assert.True(await enumerator.MoveNextAsync());

            Stopwatch stopwatch = Stopwatch.StartNew();
            Assert.True(await enumerator.MoveNextAsync());
            stopwatch.Stop();

            Assert.True(
                stopwatch.Elapsed >= TimeSpan.FromMilliseconds(40),
                $"Второй опрос начался слишком рано: {stopwatch.Elapsed}.");
        }

        [Fact]
        public void Constructor_NullDevice_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Mvk01Watcher(null!));
        }

        [Fact]
        public async Task Constructor_DefaultInterval_Is500Milliseconds()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Mvk01Watcher watcher = new(device);

            Assert.Equal(TimeSpan.FromMilliseconds(500), watcher.Interval);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task Constructor_InvalidInterval_Throws(int milliseconds)
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Mvk01Watcher(device, TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(185)]
        public void PollItem_InvalidNumber_Throws(int number)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Mvk01PollItem((Mvk01MeasurementNumber)number));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        public void PollItem_InvalidChannel_Throws(int channel)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, channel));
        }

        [Fact]
        public async Task WatchAsync_NullItems_Throws()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device);

            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await foreach (Mvk01Sample _ in watcher.WatchAsync(
                    (IReadOnlyList<Mvk01PollItem>)null!,
                    CancellationToken.None))
                {
                }
            });
        }

        [Fact]
        public async Task WatchAsync_EmptyItems_Throws()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await foreach (Mvk01Sample _ in watcher.WatchAsync(
                    Array.Empty<Mvk01PollItem>(),
                    CancellationToken.None))
                {
                }
            });
        }

        [Fact]
        public async Task WatchAsync_NullItemInList_Throws()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));
            Mvk01Watcher watcher = new(device);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await foreach (Mvk01Sample _ in watcher.WatchAsync(
                    new Mvk01PollItem[] { null! },
                    CancellationToken.None))
                {
                }
            });
        }

        private static async Task<Mvk01Sample> FirstAsync(IAsyncEnumerable<Mvk01Sample> source)
        {
            await foreach (Mvk01Sample sample in source)
            {
                return sample;
            }

            throw new InvalidOperationException("Поток завершился без результатов.");
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

        private static ushort[] EncodeFloats(params float[] values)
        {
            ushort[] registers = new ushort[values.Length * 2];

            for (int i = 0; i < values.Length; i++)
            {
                RegisterCodec.FromSingle(values[i], MvkByteOrder.BigEndian).CopyTo(registers, i * 2);
            }

            return registers;
        }

        private static void SeedFloats(MvkTestServer server, int address, params float[] values)
        {
            server.SetHoldingRegisters((ushort)address, EncodeFloats(values));
        }
    }
}
