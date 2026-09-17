using System.Linq;
using Mvk.Modbus.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Mvk.Modbus.Tests.Configuration
{
    public sealed class MvkDeviceOptionsTests
    {
        [Fact]
        public void Resolve_WithoutOverrides_AppliesDefaults()
        {
            MvkDefaultsOptions defaults = new();
            MvkDeviceOptions device = new() { Id = "mvk-1", Host = "10.20.0.204" };

            MvkDeviceSettings settings = device.Resolve(defaults);

            Assert.Equal("mvk-1", settings.Id);
            Assert.Equal("10.20.0.204", settings.Host);
            Assert.Equal(defaults.Port, settings.Port);
            Assert.Equal(defaults.SlaveId, settings.SlaveId);
            Assert.Equal(defaults.ByteOrder, settings.ByteOrder);
            Assert.Equal(defaults.ReadTimeoutMs, settings.ReadTimeoutMs);
            Assert.Equal("10.20.0.204:502", settings.Endpoint);
        }

        [Fact]
        public void Resolve_WithOverrides_UsesDeviceValues()
        {
            MvkDefaultsOptions defaults = new();
            MvkDeviceOptions device = new()
            {
                Id = "vdm-82",
                Host = "10.20.0.205",
                Port = 10502,
                SlaveId = 3,
                ByteOrder = MvkByteOrder.Mixed,
                MaxConcurrentRequests = 4,
                AllowConfigurationWrite = true
            };

            MvkDeviceSettings settings = device.Resolve(defaults);

            Assert.Equal(10502, settings.Port);
            Assert.Equal(3, settings.SlaveId);
            Assert.Equal(MvkByteOrder.Mixed, settings.ByteOrder);
            Assert.Equal(4, settings.MaxConcurrentRequests);
            Assert.True(settings.AllowConfigurationWrite);
        }

        [Fact]
        public void Resolve_Tags_CopiedAsReadOnly()
        {
            MvkDeviceOptions device = new()
            {
                Id = "mvk-1",
                Host = "10.20.0.204",
                Tags = new Dictionary<string, string> { ["asset"] = "АЭЦНСР 850" }
            };

            MvkDeviceSettings settings = device.Resolve(new MvkDefaultsOptions());

            Assert.Equal("АЭЦНСР 850", settings.Tags["ASSET"]);
        }

        [Fact]
        public void Validator_ValidConfig_Succeeds()
        {
            MvkModbusOptions options = new()
            {
                Devices =
                [
                    new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.204" },
                    new MvkDeviceOptions { Id = "mvk-2", Host = "10.20.0.205", Port = 10502 }
                ]
            };

            ValidateOptionsResult result = new MvkModbusOptionsValidator().Validate(null, options);

            Assert.False(result.Failed);
        }

        [Fact]
        public void Validator_DuplicateIds_Fails()
        {
            MvkModbusOptions options = new()
            {
                Devices =
                [
                    new MvkDeviceOptions { Id = "MVK-1", Host = "10.20.0.204" },
                    new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.205" }
                ]
            };

            ValidateOptionsResult result = new MvkModbusOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, failure => failure.Contains("не уникален"));
        }

        [Fact]
        public void Validator_MissingHostAndBadPort_Fails()
        {
            MvkModbusOptions options = new()
            {
                Devices =
                [
                    new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.204", Port = 70000 },
                    new MvkDeviceOptions { Id = "mvk-2" }
                ]
            };

            ValidateOptionsResult result = new MvkModbusOptionsValidator().Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, failure => failure.Contains("не задан адрес Host"));
            Assert.Contains(result.Failures!, failure => failure.Contains("вне допустимого диапазона"));
        }

        [Fact]
        public void Resolve_RequireEvenAlignment_InheritedFromDefaults()
        {
            MvkDefaultsOptions defaults = new() { RequireEvenAddressAndQuantity = true };
            MvkDeviceOptions device = new() { Id = "mvk-1", Host = "10.20.0.204" };

            Assert.True(device.Resolve(defaults).RequireEvenAddressAndQuantity);
            Assert.False(device.Resolve(new MvkDefaultsOptions()).RequireEvenAddressAndQuantity);
        }

        [Fact]
        public void ResolvePipeline_NotConfigured_ReturnsDefaultOrder()
        {
            MvkModbusOptions options = new();

            Assert.Null(options.Pipeline);
            Assert.Equal(
                MvkModbusOptions.CreateDefaultPipeline(),
                options.ResolvePipeline());
        }

        [Fact]
        public void ResolvePipeline_EmptyList_ReturnsEmptyPipeline()
        {
            MvkModbusOptions options = new() { Pipeline = [] };

            Assert.Empty(options.ResolvePipeline());
        }

        [Fact]
        public void ResolveDevices_NotConfigured_ReturnsEmpty()
        {
            MvkModbusOptions options = new();

            Assert.Empty(options.ResolveDevices());
        }

        [Fact]
        public void ResolveDevices_ReturnsResolvedSettings()
        {
            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions { Port = 10502, ByteOrder = MvkByteOrder.LittleEndian },
                Devices =
                [
                    new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.204" }
                ]
            };

            MvkDeviceSettings settings = options.ResolveDevices().Single();

            Assert.Equal(10502, settings.Port);
            Assert.Equal(MvkByteOrder.LittleEndian, settings.ByteOrder);
        }
    }
}
