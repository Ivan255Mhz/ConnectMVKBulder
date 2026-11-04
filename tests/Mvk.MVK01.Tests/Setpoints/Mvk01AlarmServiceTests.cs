using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.MVK01.Setpoints;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests.Setpoints
{
    public sealed class Mvk01AlarmServiceTests
    {
        [Fact]
        public async Task WatchAsync_EmitsActiveEventAndUpdatesStatus()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(() => 5f);

            await using (pool)
            {
                Mvk01DeviceFactory factory = new(pool);
                InMemoryMvkSetpointStore store = new([CreateSetpoint()]);
                await using Mvk01AlarmService service = new(
                    factory,
                    store,
                    new Mvk01AlarmOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                MvkAlarmEvent? alarm = null;

                await foreach (MvkAlarmEvent item in service.WatchAsync(cts.Token))
                {
                    alarm = item;
                    break;
                }

                Assert.NotNull(alarm);
                Assert.Equal(MvkSetpointState.Active, alarm!.State);
                Assert.Equal("mvk-1", alarm.DeviceId);
                Assert.Equal(5f, alarm.Value);
                Assert.Single(service.Statuses);
                Assert.Equal(MvkSetpointState.Active, service.Statuses[0].State);
            }
        }

        [Fact]
        public async Task RunAsync_ReactionFailure_DoesNotStopMonitoring()
        {
            int reads = 0;
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(
                () => Interlocked.Increment(ref reads) == 1 ? 5f : 1f);

            await using (pool)
            {
                Mvk01DeviceFactory factory = new(pool);
                InMemoryMvkSetpointStore store = new([CreateSetpoint()]);
                int reactionErrors = 0;

                Mvk01AlarmOptions options = new()
                {
                    PollInterval = TimeSpan.FromMilliseconds(10),
                    OnReactionError = (_, _) => reactionErrors++
                };

                await using Mvk01AlarmService service = new(factory, store, options);

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
                List<MvkSetpointState> states = [];

                try
                {
                    await service.RunAsync((alarm, _) =>
                    {
                        states.Add(alarm.State);

                        if (alarm.State == MvkSetpointState.Active)
                        {
                            throw new InvalidOperationException("Тестовая ошибка реакции.");
                        }

                        if (alarm.State == MvkSetpointState.Normal)
                        {
                            cts.Cancel();
                        }

                        return ValueTask.CompletedTask;
                    }, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }

                Assert.Equal(2, states.Count);
                Assert.Equal(MvkSetpointState.Active, states[0]);
                Assert.Equal(MvkSetpointState.Normal, states[1]);
                Assert.Equal(1, reactionErrors);
            }
        }

        [Fact]
        public async Task ReloadSetpointsAsync_WhileWatching_Throws()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(() => 5f);

            await using (pool)
            {
                Mvk01DeviceFactory factory = new(pool);
                InMemoryMvkSetpointStore store = new([CreateSetpoint()]);
                await using Mvk01AlarmService service = new(
                    factory,
                    store,
                    new Mvk01AlarmOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                await using IAsyncEnumerator<MvkAlarmEvent> enumerator = service
                    .WatchAsync(cts.Token)
                    .GetAsyncEnumerator();

                Assert.True(await enumerator.MoveNextAsync());
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => service.ReloadSetpointsAsync());
            }
        }

        [Fact]
        public async Task StartAsync_LoadsJsonAndStartsMonitoring()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedMeasurement(server, 5f);

            string devicesPath = CreateTempPath(".devices.json");
            string setpointsPath = CreateTempPath(".setpoints.json");

            try
            {
                await File.WriteAllTextAsync(devicesPath, CreateDevicesJson(server.Port));
                await File.WriteAllTextAsync(setpointsPath, CreateSetpointsJson());

                await using Mvk01AlarmService service = await Mvk01AlarmService.StartAsync(
                    devicesPath,
                    setpointsPath,
                    new Mvk01AlarmOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                MvkAlarmEvent? alarm = null;

                await foreach (MvkAlarmEvent item in service.WatchAsync(cts.Token))
                {
                    alarm = item;
                    break;
                }

                Assert.NotNull(alarm);
                Assert.Equal(MvkSetpointState.Active, alarm!.State);
                Assert.Equal("mvk-1", alarm.DeviceId);
                Assert.Equal(5f, alarm.Value);
            }
            finally
            {
                DeleteFile(devicesPath);
                DeleteFile(setpointsPath);
            }
        }

        [Fact]
        public async Task StartAsync_WithFluentSetpoints_Works()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedMeasurement(server, 5f);

            string devicesPath = CreateTempPath(".devices.json");

            try
            {
                await File.WriteAllTextAsync(devicesPath, CreateDevicesJson(server.Port));

                MvkSetpointSet setpoints = MvkSetpointSet.Create()
                    .Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 1)
                        .Warning(4.5, hysteresis: 0.2);

                await using Mvk01AlarmService service = await Mvk01AlarmService.StartAsync(
                    devicesPath,
                    setpoints,
                    new Mvk01AlarmOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
                MvkAlarmEvent? alarm = null;

                await foreach (MvkAlarmEvent item in service.WatchAsync(cts.Token))
                {
                    alarm = item;
                    break;
                }

                Assert.NotNull(alarm);
                Assert.Equal(MvkSetpointState.Active, alarm!.State);
                Assert.Equal(5f, alarm.Value);
            }
            finally
            {
                DeleteFile(devicesPath);
            }
        }

        private static (MvkDevicePool Pool, FakeModbusTransport Transport) CreateFakePool(
            Func<float> valueFactory)
        {
            FakeModbusTransport transport = new(request =>
            {
                if (request.IsWrite)
                {
                    return ModbusResponse.WriteSuccess();
                }

                return ModbusResponse.Success(EncodeFloats(valueFactory()));
            });

            FakeModbusTransportFactory transportFactory = new(_ => transport);

            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions { RequireEvenAddressAndQuantity = true },
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return (new MvkDevicePool(options, transportFactory, middlewares: []), transport);
        }

        private static MvkSetpoint CreateSetpoint()
        {
            return new MvkSetpoint
            {
                Id = "mvk-1/ch1/warning",
                DeviceId = "mvk-1",
                Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                Channel = 1,
                Comparison = MvkSetpointComparison.Above,
                Threshold = 4.5,
                Hysteresis = 0.2,
                Severity = MvkAlarmSeverity.Warning,
                Name = "Канал 1"
            };
        }

        private static ushort[] EncodeFloats(float first)
        {
            float[] values = [first, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
            ushort[] registers = new ushort[values.Length * 2];

            for (int index = 0; index < values.Length; index++)
            {
                RegisterCodec
                    .FromSingle(values[index], MvkByteOrder.BigEndian)
                    .CopyTo(registers, index * 2);
            }

            return registers;
        }

        private static void SeedMeasurement(MvkTestServer server, float first)
        {
            server.SetHoldingRegisters(
                Mvk01RegisterMap.MeasurementByNumber(
                    (int)Mvk01MeasurementNumber.Rms10To1000Acceleration),
                EncodeFloats(first));
        }

        private static string CreateDevicesJson(int port)
        {
            return $$"""
            {
              "MvkModbus": {
                "Defaults": {
                  "Port": {{port}},
                  "RequireEvenAddressAndQuantity": true
                },
                "Pool": { "ReconnectEnabled": false },
                "Devices": [
                  { "Id": "mvk-1", "Host": "127.0.0.1" }
                ]
              }
            }
            """;
        }

        private static string CreateSetpointsJson()
        {
            return """
            {
              "setpoints": [
                {
                  "id": "mvk-1/ch1/warning",
                  "deviceId": "mvk-1",
                  "measurement": "Rms10To1000Acceleration",
                  "channel": 1,
                  "comparison": "Above",
                  "threshold": 4.5,
                  "hysteresis": 0.2,
                  "severity": "Warning",
                  "name": "Канал 1"
                }
              ]
            }
            """;
        }

        private static string CreateTempPath(string suffix)
        {
            return Path.Combine(
                Path.GetTempPath(),
                "mvk-alarm-" + Guid.NewGuid().ToString("N") + suffix);
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
