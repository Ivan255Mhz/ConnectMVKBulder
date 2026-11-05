using System;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01ReadingTests
    {
        [Fact]
        public async Task ReadChannelAsync_KeepsLegacyFiveRequests()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                await device.ReadChannelAsync(1);

                Assert.Collection(
                    transport.Requests,
                    request => AssertChunk(request, 0x2000, 96),
                    request => AssertChunk(request, 0x2060, 48),
                    request => AssertChunk(request, 0x209A, 6),
                    request => AssertChunk(request, 0x20A0, 48),
                    request => AssertChunk(request, 0x20E0, 48));
            }
        }

        [Fact]
        public async Task ReadChannelSnapshotAsync_UsesThreeRequestsAndChecksCopies()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01Channel snapshot = await device.ReadChannelSnapshotAsync(1);

                Assert.True(snapshot.IsConsistent);
                Assert.Equal(1, snapshot.ReadAttempts);
                Assert.NotEqual(default, snapshot.CapturedAt);
                Assert.Collection(
                    transport.Requests,
                    request => AssertChunk(request, 0x2000, 96),
                    request => AssertChunk(request, 0x2060, 124),
                    request => AssertChunk(request, 0x20DC, 70));
            }
        }

        [Fact]
        public async Task ReadChannelSnapshotAsync_FastMode_SkipsConsistencyCheck()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);
            Mvk01SnapshotOptions options = new()
            {
                Consistency = Mvk01SnapshotConsistencyMode.Fast
            };

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"), snapshotOptions: options);

                Mvk01Channel snapshot = await device.ReadChannelSnapshotAsync(1);

                Assert.Null(snapshot.IsConsistent);
                Assert.Equal(1, snapshot.ReadAttempts);
                Assert.Equal(3, transport.Requests.Count);
            }
        }

        [Fact]
        public async Task ReadChannelSnapshotAsync_InconsistentCopies_RetriesAndMarksSnapshot()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(InconsistentHandler);
            Mvk01SnapshotOptions options = new() { MaxChannelReadAttempts = 2 };

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"), snapshotOptions: options);

                Mvk01Channel snapshot = await device.ReadChannelSnapshotAsync(1);

                Assert.False(snapshot.IsConsistent);
                Assert.Equal(2, snapshot.ReadAttempts);
                Assert.Equal(6, transport.Requests.Count);
            }
        }

        [Fact]
        public async Task ReadChannelSnapshotAsync_ThrowOnInconsistent_Throws()
        {
            (MvkDevicePool pool, _) = CreateFakePool(InconsistentHandler);
            Mvk01SnapshotOptions options = new()
            {
                MaxChannelReadAttempts = 2,
                ThrowOnInconsistentSnapshot = true
            };

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"), snapshotOptions: options);

                MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
                    () => device.ReadChannelSnapshotAsync(1));

                Assert.Equal(MvkErrorKind.Validation, exception.ErrorKind);
            }
        }

        [Fact]
        public async Task ReadChannelSnapshotAsync_FallsBackToSplitReads()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(
                FallbackHandler(failQuantity: 124));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01Channel snapshot = await device.ReadChannelSnapshotAsync(1);

                Assert.Null(snapshot.IsConsistent);
                Assert.Equal(1, snapshot.ReadAttempts);
                Assert.Equal(7, transport.Requests.Count);
            }
        }

        [Fact]
        public async Task ReadDeviceInfoAsync_UsesSingleRequest()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();

                Assert.NotEqual(default, info.CapturedAt);
                AssertChunk(Assert.Single(transport.Requests), 0x1000, 38);
            }
        }

        [Fact]
        public async Task ReadDeviceInfoAsync_FallsBackToSplitReads()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(
                FallbackHandler(failQuantity: 38));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                await device.ReadDeviceInfoAsync();

                Assert.Equal(4, transport.Requests.Count);
            }
        }

        [Fact]
        public async Task ReadChannelConfigAsync_UsesSingleRequest()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(ZeroHandler);

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01ChannelConfig config = await device.ReadChannelConfigAsync(3);

                Assert.NotEqual(default, config.CapturedAt);
                AssertChunk(
                    Assert.Single(transport.Requests),
                    Mvk01RegisterMap.ChannelConfig(3),
                    84);
            }
        }

        [Fact]
        public async Task ReadChannelConfigAsync_FallsBackToSplitReads()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(
                FallbackHandler(failQuantity: 84));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                await device.ReadChannelConfigAsync(2);

                Assert.Equal(6, transport.Requests.Count);
            }
        }

        private static ModbusResponse ZeroHandler(ModbusRequest request)
        {
            return request.IsWrite
                ? ModbusResponse.WriteSuccess()
                : ModbusResponse.Success(new ushort[request.Quantity]);
        }

        private static ModbusResponse InconsistentHandler(ModbusRequest request)
        {
            ushort[] data = new ushort[request.Quantity];

            switch (request.StartAddress)
            {
                case 0x2000:
                    RegisterCodec.FromSingle(1.0f, MvkByteOrder.BigEndian).CopyTo(data, 0);
                    break;

                case 0x2060:
                    RegisterCodec.FromSingle(2.0f, MvkByteOrder.BigEndian).CopyTo(data, 0x3C);
                    break;
            }

            return ModbusResponse.Success(data);
        }

        private static Func<ModbusRequest, ModbusResponse> FallbackHandler(ushort failQuantity)
        {
            return request =>
            {
                if (!request.IsWrite && request.Quantity == failQuantity)
                {
                    return ModbusResponse.Failure(new MvkModbusException(
                        "Illegal data address",
                        MvkErrorKind.DeviceError,
                        "mvk-1",
                        0x02));
                }

                return ZeroHandler(request);
            };
        }

        private static void AssertChunk(ModbusRequest request, ushort address, ushort quantity)
        {
            Assert.False(request.IsWrite);
            Assert.Equal(address, request.StartAddress);
            Assert.Equal(quantity, request.Quantity);
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
