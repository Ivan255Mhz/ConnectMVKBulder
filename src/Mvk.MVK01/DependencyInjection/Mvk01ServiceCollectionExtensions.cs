using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.DependencyInjection;
using Mvk.Modbus.Pool;

namespace Mvk.MVK01.DependencyInjection
{
    /// <summary>
    /// Регистрация типизированного слоя МВК01 в DI-контейнере.
    /// </summary>
    public static class Mvk01ServiceCollectionExtensions
    {
        /// <summary>
        /// Одним вызовом регистрирует пул МВК и типизированный слой МВК01.
        /// Эквивалентно <c>AddMvkModbus(...)</c> + <c>AddMvk01Devices()</c>.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="configuration">Конфигурация с секцией <c>MvkModbus</c>.</param>
        /// <param name="sectionName">Имя секции конфигурации.</param>
        public static IServiceCollection AddMvk01(
            this IServiceCollection services,
            IConfiguration configuration,
            string sectionName = MvkModbusServiceCollectionExtensions.DefaultSectionName)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            services.AddMvkModbus(configuration, sectionName);
            services.AddMvk01Devices();
            return services;
        }

        /// <summary>
        /// Одним вызовом регистрирует пул МВК и типизированный слой МВК01
        /// с программной настройкой.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="configure">Настройка конфигурации.</param>
        public static IServiceCollection AddMvk01(
            this IServiceCollection services,
            Action<MvkModbusOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            services.AddMvkModbus(configure);
            services.AddMvk01Devices();
            return services;
        }

        /// <summary>
        /// Регистрирует фабрику типизированных устройств МВК01 и включает обязательную
        /// для этого устройства настройку чётности адреса и количества регистров.
        /// Запись конфигурации по умолчанию запрещена; чтобы разрешить её, задайте
        /// <see cref="MvkDefaultsOptions.AllowConfigurationWrite"/> явно в конфигурации
        /// или в <c>AddMvkModbus</c>.
        /// Вызывать после <see cref="MvkModbusServiceCollectionExtensions.AddMvkModbus(IServiceCollection, Action{MvkModbusOptions})"/>.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        public static IServiceCollection AddMvk01Devices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<MvkModbusOptions>().PostConfigure(options =>
            {
                options.Defaults.RequireEvenAddressAndQuantity = true;
            });

            services.TryAddSingleton<IMvk01DeviceFactory>(provider =>
                new Mvk01DeviceFactory(provider.GetRequiredService<IMvkDevicePool>()));

            return services;
        }
    }
}
