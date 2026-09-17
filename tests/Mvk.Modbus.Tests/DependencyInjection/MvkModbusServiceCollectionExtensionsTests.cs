using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.DependencyInjection;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.DependencyInjection
{
    public sealed class MvkModbusServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddMvkModbus_ConfigureAction_RegistersPoolWithDevices()
        {
            using ServiceProvider provider = BuildProvider(services => services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "10.20.0.211" }];
            }));

            IMvkDevicePool pool = provider.GetRequiredService<IMvkDevicePool>();

            Assert.Single(pool.Devices);
            Assert.Equal("mvk-1", pool.Devices.First().Settings.Id);
            Assert.Same(pool, provider.GetRequiredService<MvkDevicePool>());
        }

        [Fact]
        public void AddMvkModbus_Configuration_BindsSection()
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MvkModbus:Defaults:Port"] = "10502",
                    ["MvkModbus:Defaults:ByteOrder"] = "Mixed",
                    ["MvkModbus:Pipeline:0"] = "validation",
                    ["MvkModbus:Devices:0:Id"] = "mvk-1",
                    ["MvkModbus:Devices:0:Host"] = "10.20.0.211"
                })
                .Build();

            using ServiceProvider provider = BuildProvider(services => services.AddMvkModbus(configuration));

            IMvkDevicePool pool = provider.GetRequiredService<IMvkDevicePool>();
            MvkDeviceSettings settings = pool.Devices.Single().Settings;

            Assert.Equal(10502, settings.Port);
            Assert.Equal(MvkByteOrder.Mixed, settings.ByteOrder);
            Assert.Equal("10.20.0.211", settings.Host);
        }

        [Fact]
        public void AddMvkModbus_InvalidConfiguration_ThrowsOptionsValidationException()
        {
            using ServiceProvider provider = BuildProvider(services => services.AddMvkModbus(options =>
            {
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1" }];
            }));

            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IMvkDevicePool>());
        }

        [Fact]
        public void AddMvkModbus_UnknownPipelineName_ThrowsConfigurationError()
        {
            using ServiceProvider provider = BuildProvider(services => services.AddMvkModbus(options =>
            {
                options.Pipeline = ["неизвестный"];
                options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }];
            }));

            MvkModbusException exception = Assert.Throws<MvkModbusException>(
                () => provider.GetRequiredService<IMvkDevicePool>());

            Assert.Equal(MvkErrorKind.Configuration, exception.ErrorKind);
        }

        [Fact]
        public async Task AddModbusMiddleware_CustomMiddlewareParticipatesInPipeline()
        {
            MiddlewareLog log = new();

            using ServiceProvider provider = BuildProvider(services =>
            {
                services.AddSingleton(log);
                services.AddModbusMiddleware<RecordingMiddleware>("recording");
                services.AddMvkModbus(options =>
                {
                    options.Pipeline = ["recording"];
                    options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }];
                });
                services.AddSingleton<Modbus.Transport.IMvkTransportFactory>(
                    new FakeTransportFactory(_ => new FakeTransport()));
            });

            IMvkDevicePool pool = provider.GetRequiredService<IMvkDevicePool>();
            IMvkDevice device = pool.GetDevice("mvk-1");

            ModbusResponse response = await device.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));

            Assert.True(response.IsSuccess);
            Assert.Equal(["recording"], log.Entries);
        }

        [Fact]
        public void AddModbusMiddleware_DuplicateName_ReplacesRegistration()
        {
            using ServiceProvider provider = BuildProvider(services =>
            {
                services.AddSingleton<MiddlewareLog>();
                services.AddModbusMiddleware<RecordingMiddleware>("audit");
                services.AddModbusMiddleware<OtherMiddleware>("audit");
                services.AddMvkModbus(options =>
                {
                    options.Pipeline = ["audit"];
                    options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }];
                });
            });

            MvkMiddlewareRegistry registry = provider.GetRequiredService<MvkMiddlewareRegistry>();

            Assert.True(registry.TryGetType("audit", out System.Type? type));
            Assert.Equal(typeof(OtherMiddleware), type);
        }

        [Fact]
        public void AddMvkModbusHostedService_RegistersWarmupService()
        {
            using ServiceProvider provider = BuildProvider(services =>
            {
                services.AddMvkModbus(options =>
                {
                    options.Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }];
                });
                services.AddMvkModbusHostedService();
            });

            Assert.Contains(
                provider.GetServices<IHostedService>(),
                service => service is MvkModbusWarmupHostedService);
        }

        private static ServiceProvider BuildProvider(System.Action<IServiceCollection> configure)
        {
            ServiceCollection services = new();
            services.AddLogging();
            configure(services);
            return services.BuildServiceProvider();
        }

        private sealed class MiddlewareLog
        {
            public List<string> Entries { get; } = [];
        }

        private sealed class RecordingMiddleware : IModbusMiddleware
        {
            private readonly MiddlewareLog log;

            public RecordingMiddleware(MiddlewareLog log)
            {
                this.log = log;
            }

            public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
            {
                log.Entries.Add("recording");
                return next(context, cancellationToken);
            }
        }

        private sealed class OtherMiddleware : IModbusMiddleware
        {
            public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
            {
                return next(context, cancellationToken);
            }
        }
    }
}
