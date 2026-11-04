using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Mvk.MVK01.Setpoints;
using Xunit;

namespace Mvk.MVK01.Tests.Setpoints
{
    public sealed class MvkSetpointStoreTests
    {
        [Fact]
        public async Task JsonStore_RoundTrip_PreservesAllFields()
        {
            string path = CreateTempPath();

            try
            {
                MvkSetpoint source = new()
                {
                    Id = "mvk-1/ch1/warning",
                    DeviceId = "mvk-1",
                    Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                    Channel = 1,
                    Comparison = MvkSetpointComparison.Outside,
                    Threshold = 0.5,
                    UpperThreshold = 8.0,
                    Hysteresis = 0.2,
                    ActivationDelay = TimeSpan.FromSeconds(1),
                    Severity = MvkAlarmSeverity.Alarm,
                    Name = "Канал 1",
                    Message = "Вышел за диапазон",
                    Tags = new Dictionary<string, string> { ["asset"] = "stand-1" }
                };

                JsonMvkSetpointStore store = new(path);
                await store.SaveAsync([source]);
                IReadOnlyList<MvkSetpoint> loaded = await store.LoadAsync();

                MvkSetpoint result = Assert.Single(loaded);
                Assert.Equal(source.Id, result.Id);
                Assert.Equal(source.DeviceId, result.DeviceId);
                Assert.Equal(source.Measurement, result.Measurement);
                Assert.Equal(source.Channel, result.Channel);
                Assert.Equal(source.Comparison, result.Comparison);
                Assert.Equal(source.Threshold, result.Threshold);
                Assert.Equal(source.UpperThreshold, result.UpperThreshold);
                Assert.Equal(source.Hysteresis, result.Hysteresis);
                Assert.Equal(source.ActivationDelay, result.ActivationDelay);
                Assert.Equal(source.Severity, result.Severity);
                Assert.Equal(source.Name, result.Name);
                Assert.Equal(source.Message, result.Message);
                Assert.Equal("stand-1", result.Tags!["asset"]);
            }
            finally
            {
                DeleteTempFiles(path);
            }
        }

        [Fact]
        public async Task JsonStore_LoadsArrayFile()
        {
            string path = CreateTempPath();

            try
            {
                string json = """
                [
                  {
                    "id": "a",
                    "deviceId": "mvk-1",
                    "measurement": "Rms10To1000Acceleration",
                    "channel": 1,
                    "comparison": "Above",
                    "threshold": 4.5,
                    "hysteresis": 0.2
                  }
                ]
                """;

                await File.WriteAllTextAsync(path, json);

                JsonMvkSetpointStore store = new(path);
                IReadOnlyList<MvkSetpoint> loaded = await store.LoadAsync();

                MvkSetpoint setpoint = Assert.Single(loaded);
                Assert.Equal("a", setpoint.Id);
                Assert.Equal(MvkSetpointComparison.Above, setpoint.Comparison);
            }
            finally
            {
                DeleteTempFiles(path);
            }
        }

        [Fact]
        public async Task JsonStore_MissingFile_Throws()
        {
            JsonMvkSetpointStore store = new(CreateTempPath());

            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await store.LoadAsync());
        }

        [Fact]
        public async Task JsonStore_InvalidJson_Throws()
        {
            string path = CreateTempPath();

            try
            {
                await File.WriteAllTextAsync(path, "{ not json }");
                JsonMvkSetpointStore store = new(path);

                await Assert.ThrowsAnyAsync<JsonException>(async () => await store.LoadAsync());
            }
            finally
            {
                DeleteTempFiles(path);
            }
        }

        [Fact]
        public async Task JsonStore_SaveInvalidSet_Throws()
        {
            string path = CreateTempPath();

            try
            {
                JsonMvkSetpointStore store = new(path);
                MvkSetpoint invalid = CreateSetpoint("x") with { Channel = 9 };

                await Assert.ThrowsAsync<MvkSetpointValidationException>(
                    async () => await store.SaveAsync([invalid]));
            }
            finally
            {
                DeleteTempFiles(path);
            }
        }

        [Fact]
        public async Task InMemoryStore_RoundTrip_ReplacesContent()
        {
            InMemoryMvkSetpointStore store = new([CreateSetpoint("a")]);

            Assert.Single(await store.LoadAsync());

            await store.SaveAsync([CreateSetpoint("b"), CreateSetpoint("c")]);
            IReadOnlyList<MvkSetpoint> loaded = await store.LoadAsync();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("b", loaded[0].Id);
            Assert.Equal("c", loaded[1].Id);
        }

        private static MvkSetpoint CreateSetpoint(string id)
        {
            return new MvkSetpoint
            {
                Id = id,
                DeviceId = "mvk-1",
                Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                Channel = 1,
                Comparison = MvkSetpointComparison.Above,
                Threshold = 4.5
            };
        }

        private static string CreateTempPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "mvk-setpoints-" + Guid.NewGuid().ToString("N") + ".json");
        }

        private static void DeleteTempFiles(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(path + ".tmp"))
            {
                File.Delete(path + ".tmp");
            }
        }
    }
}
