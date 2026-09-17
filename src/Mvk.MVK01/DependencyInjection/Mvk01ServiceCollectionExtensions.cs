using System;
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
        /// Регистрирует фабрику типизированных устройств МВК01 и включает обязательные
        /// для этого устройства настройки: чётные адрес/количество регистров и разрешение
        /// записи конфигурации (команды Save/Reboot).
        /// Вызывать после <see cref="MvkModbusServiceCollectionExtensions.AddMvkModbus(IServiceCollection, Action{MvkModbusOptions})"/>.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        public static IServiceCollection AddMvk01Devices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<MvkModbusOptions>().PostConfigure(options =>
            {
                options.Defaults.RequireEvenAddressAndQuantity = true;
                options.Defaults.AllowConfigurationWrite = true;
            });

            services.TryAddSingleton<IMvk01DeviceFactory>(provider =>
                new Mvk01DeviceFactory(provider.GetRequiredService<IMvkDevicePool>()));

            return services;
        }
    }
}
