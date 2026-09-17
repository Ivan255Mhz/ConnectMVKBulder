using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Transport;

namespace Mvk.Modbus.DependencyInjection
{
    /// <summary>
    /// Регистрация библиотеки в DI-контейнере.
    /// </summary>
    public static class MvkModbusServiceCollectionExtensions
    {
        /// <summary>
        /// Имя секции конфигурации по умолчанию.
        /// </summary>
        public const string DefaultSectionName = "MvkModbus";

        /// <summary>
        /// Регистрирует пул устройств с привязкой секции <paramref name="sectionName"/> конфигурации.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="configuration">Конфигурация (appsettings.json и т.п.).</param>
        /// <param name="sectionName">Имя секции конфигурации.</param>
        public static IServiceCollection AddMvkModbus(
            this IServiceCollection services,
            IConfiguration configuration,
            string sectionName = DefaultSectionName)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return services.AddMvkModbusCore(options =>
                configuration.GetSection(sectionName).Bind(options));
        }

        /// <summary>
        /// Регистрирует пул устройств с программной настройкой.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="configure">Настройка конфигурации.</param>
        public static IServiceCollection AddMvkModbus(
            this IServiceCollection services,
            Action<MvkModbusOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            return services.AddMvkModbusCore(configure);
        }

        /// <summary>
        /// Регистрирует пользовательский middleware под именем для секции <c>Pipeline</c>.
        /// Если имя не задано, используется имя типа.
        /// </summary>
        /// <typeparam name="TMiddleware">Тип middleware с конструктором, разрешаемым через DI.</typeparam>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="name">Имя для секции <c>Pipeline</c>.</param>
        public static IServiceCollection AddModbusMiddleware<TMiddleware>(
            this IServiceCollection services,
            string? name = null)
            where TMiddleware : class, IModbusMiddleware
        {
            string key = string.IsNullOrWhiteSpace(name) ? typeof(TMiddleware).Name : name.Trim();

            GetOrAddRegistry(services).Add(key, typeof(TMiddleware));
            return services;
        }

        /// <summary>
        /// Добавляет hosted-сервис, который при старте хоста инициализирует пул и пишет в лог список устройств.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        public static IServiceCollection AddMvkModbusHostedService(this IServiceCollection services)
        {
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedService, MvkModbusWarmupHostedService>());
            return services;
        }

        private static IServiceCollection AddMvkModbusCore(
            this IServiceCollection services,
            Action<MvkModbusOptions> configureOptions)
        {
            services.AddOptions<MvkModbusOptions>().Configure(configureOptions);
            services.AddOptions<MvkModbusOptions>().ValidateOnStart();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<MvkModbusOptions>, MvkModbusOptionsValidator>());

            services.TryAddSingleton<IMvkTransportFactory>(provider =>
                new NModbusTransportFactory(provider.GetService<ILoggerFactory>()));

            services.TryAddSingleton(provider =>
            {
                MvkModbusOptions options = provider.GetRequiredService<IOptions<MvkModbusOptions>>().Value;
                var middlewares = MvkMiddlewareResolver.ResolveMiddlewares(options, provider);

                return new MvkDevicePool(
                    options,
                    provider.GetRequiredService<IMvkTransportFactory>(),
                    middlewares,
                    provider.GetService<ILoggerFactory>());
            });

            services.TryAddSingleton<IMvkDevicePool>(provider =>
                provider.GetRequiredService<MvkDevicePool>());

            return services;
        }

        private static MvkMiddlewareRegistry GetOrAddRegistry(IServiceCollection services)
        {
            foreach (ServiceDescriptor descriptor in services)
            {
                if (descriptor.ServiceType == typeof(MvkMiddlewareRegistry)
                    && descriptor.ImplementationInstance is MvkMiddlewareRegistry existing)
                {
                    return existing;
                }
            }

            MvkMiddlewareRegistry registry = new();
            services.AddSingleton(registry);
            return registry;
        }
    }
}
