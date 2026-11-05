using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mvk.MVK01.DependencyInjection;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.DependencyInjection;
using Xunit;

namespace Mvk.MVK01.Tests.DependencyInjection
{
    public sealed class Mvk01ServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddMvk01Devices_RegistersFactoryAndEnablesRequiredDefaults()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();

            using ServiceProvider provider = services.BuildServiceProvider();

            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();
            IMvk01Device device = factory.GetDevice("mvk-1");

            Assert.Equal("mvk-1", device.Id);
            Assert.Single(factory.Devices);

            MvkModbusOptions options = provider.GetRequiredService<IOptions<MvkModbusOptions>>().Value;
            Assert.True(options.Defaults.RequireEvenAddressAndQuantity);
            Assert.False(options.Defaults.AllowConfigurationWrite);
        }

        [Fact]
        public void AddMvk01_RegistersPoolAndTypedFactory()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvk01(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();
            Assert.Single(factory.Devices);

            MvkModbusOptions options = provider.GetRequiredService<IOptions<MvkModbusOptions>>().Value;
            Assert.True(options.Defaults.RequireEvenAddressAndQuantity);
        }

        [Fact]
        public void AddMvk01Devices_ExplicitWritePermission_IsPreserved()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Defaults.AllowConfigurationWrite = true;
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();

            using ServiceProvider provider = services.BuildServiceProvider();

            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();
            IMvk01Device device = factory.GetDevice("mvk-1");

            Assert.True(device.Settings.AllowConfigurationWrite);
        }

        [Fact]
        public void AddMvk01Devices_ConfiguresSnapshotOptions()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices(options =>
            {
                options.Consistency = Mvk01SnapshotConsistencyMode.Fast;
                options.MaxMeasurementBatchBlocks = 3;
            });

            using ServiceProvider provider = services.BuildServiceProvider();

            Mvk01SnapshotOptions snapshotOptions = provider.GetRequiredService<Mvk01SnapshotOptions>();
            Assert.Equal(Mvk01SnapshotConsistencyMode.Fast, snapshotOptions.Consistency);
            Assert.Equal(3, snapshotOptions.MaxMeasurementBatchBlocks);

            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();
            Mvk01Device device = Assert.IsType<Mvk01Device>(factory.GetDevice("mvk-1"));
            Assert.Same(snapshotOptions, device.SnapshotOptions);
        }

        [Fact]
        public void GetDevice_Unknown_ThrowsDeviceNotFound()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();

            using ServiceProvider provider = services.BuildServiceProvider();
            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();

            Mvk.Modbus.MvkModbusException exception = Assert.Throws<Mvk.Modbus.MvkModbusException>(
                () => factory.GetDevice("unknown"));

            Assert.Equal(Mvk.Modbus.MvkErrorKind.DeviceNotFound, exception.ErrorKind);
        }

        [Fact]
        public void TryGetDevice_Unknown_ReturnsFalse()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            });
            services.AddMvk01Devices();

            using ServiceProvider provider = services.BuildServiceProvider();
            IMvk01DeviceFactory factory = provider.GetRequiredService<IMvk01DeviceFactory>();

            Assert.False(factory.TryGetDevice("unknown", out _));
        }
    }
}
