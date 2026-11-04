using Microsoft.Extensions.DependencyInjection;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.DependencyInjection;
using Mvk.MVK01.DependencyInjection;
using Mvk.MVK01.Setpoints;
using Xunit;

namespace Mvk.MVK01.Tests.DependencyInjection
{
    public sealed class Mvk01AlarmServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddMvk01Alarms_RegistersServiceAndJsonStore()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();
            services.AddMvk01Alarms(options => options.SetpointsFile = "custom-setpoints.json");

            using ServiceProvider provider = services.BuildServiceProvider();

            IMvk01AlarmService service = provider.GetRequiredService<IMvk01AlarmService>();
            Assert.NotNull(service);

            JsonMvkSetpointStore store =
                Assert.IsType<JsonMvkSetpointStore>(provider.GetRequiredService<IMvkSetpointStore>());
            Assert.EndsWith("custom-setpoints.json", store.Path);
        }

        [Fact]
        public void AddMvk01Alarms_DoesNotReplaceCustomStore()
        {
            InMemoryMvkSetpointStore customStore = new(
            [
                new MvkSetpoint
                {
                    Id = "sp",
                    DeviceId = "mvk-1",
                    Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                    Channel = 1,
                    Comparison = MvkSetpointComparison.Above,
                    Threshold = 4.5
                }
            ]);

            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();
            services.AddSingleton<IMvkSetpointStore>(customStore);
            services.AddMvk01Alarms();

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Same(customStore, provider.GetRequiredService<IMvkSetpointStore>());
            Assert.NotNull(provider.GetRequiredService<IMvk01AlarmService>());
        }
    }
}
