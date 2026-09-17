using System;
using System.IO;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Xunit;

namespace Mvk.Modbus.Tests.Configuration
{
    public sealed class MvkModbusOptionsLoaderTests : IDisposable
    {
        private readonly string tempDirectory;

        public MvkModbusOptionsLoaderTests()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "mvk-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Fact]
        public void LoadFromJsonFile_ValidConfig_LoadsDevicesAndPipeline()
        {
            string path = WriteJson(
                """
                {
                  "MvkModbus": {
                    "Defaults": { "Port": 10502, "ByteOrder": "Mixed" },
                    "Pipeline": ["logging", "retry"],
                    "Devices": [
                      {
                        "Id": "mvk-1",
                        "Name": "Агрегат 1",
                        "Host": "10.20.0.204",
                        "Tags": { "asset": "АЭЦНСР 850" }
                      }
                    ]
                  }
                }
                """);

            MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile(path);

            Assert.Equal(["logging", "retry"], options.ResolvePipeline());
            MvkDeviceSettings settings = Assert.Single(options.ResolveDevices());
            Assert.Equal("mvk-1", settings.Id);
            Assert.Equal("Агрегат 1", settings.Name);
            Assert.Equal(10502, settings.Port);
            Assert.Equal(MvkByteOrder.Mixed, settings.ByteOrder);
            Assert.Equal("АЭЦНСР 850", settings.Tags["asset"]);
        }

        [Fact]
        public void LoadFromJsonFile_NoPipelineKey_ReturnsDefaultOrder()
        {
            string path = WriteJson(
                """
                {
                  "MvkModbus": {
                    "Devices": [ { "Id": "mvk-1", "Host": "10.20.0.204" } ]
                  }
                }
                """);

            MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile(path);

            Assert.Equal(MvkModbusOptions.CreateDefaultPipeline(), options.ResolvePipeline());
        }

        [Fact]
        public void LoadFromJsonFile_InvalidConfig_ThrowsConfigurationError()
        {
            string path = WriteJson(
                """
                {
                  "MvkModbus": {
                    "Devices": [ { "Id": "mvk-1", "Host": "" } ]
                  }
                }
                """);

            MvkModbusException exception = Assert.Throws<MvkModbusException>(
                () => MvkModbusOptionsLoader.LoadFromJsonFile(path));

            Assert.Equal(MvkErrorKind.Configuration, exception.ErrorKind);
            Assert.Contains("Host", exception.Message);
        }

        [Fact]
        public void LoadFromJsonFile_MissingSection_ThrowsConfigurationError()
        {
            string path = WriteJson("""{ "Other": { } }""");

            MvkModbusException exception = Assert.Throws<MvkModbusException>(
                () => MvkModbusOptionsLoader.LoadFromJsonFile(path));

            Assert.Equal(MvkErrorKind.Configuration, exception.ErrorKind);
        }

        [Fact]
        public void LoadFromJsonFile_MissingFile_ThrowsConfigurationError()
        {
            string path = Path.Combine(tempDirectory, "absent.json");

            MvkModbusException exception = Assert.Throws<MvkModbusException>(
                () => MvkModbusOptionsLoader.LoadFromJsonFile(path));

            Assert.Equal(MvkErrorKind.Configuration, exception.ErrorKind);
        }

        private string WriteJson(string content)
        {
            string path = Path.Combine(tempDirectory, "mvkmodbus.json");
            File.WriteAllText(path, content);
            return path;
        }
    }
}
