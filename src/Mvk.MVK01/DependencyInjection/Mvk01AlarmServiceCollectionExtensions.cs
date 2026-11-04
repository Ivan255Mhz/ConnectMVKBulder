using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mvk.MVK01.Setpoints;

namespace Mvk.MVK01.DependencyInjection
{
    /// <summary>
    /// Регистрация высокоуровневого сервиса уставок в DI.
    /// </summary>
    public static class Mvk01AlarmServiceCollectionExtensions
    {
        /// <summary>
        /// Регистрирует <see cref="IMvkSetpointStore"/> (JSON по умолчанию)
        /// и <see cref="IMvk01AlarmService"/>.
        /// Вызывать после <see cref="Mvk01ServiceCollectionExtensions.AddMvk01Devices"/>.
        /// </summary>
        /// <param name="services">Коллекция служб.</param>
        /// <param name="configure">Настройка сервиса.</param>
        public static IServiceCollection AddMvk01Alarms(
            this IServiceCollection services,
            Action<Mvk01AlarmOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            if (configure is not null)
            {
                services.Configure(configure);
            }

            services.AddOptions<Mvk01AlarmOptions>()
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.SetpointsFile),
                    "Не задан путь к файлу уставок.")
                .Validate(
                    options => options.PollInterval > TimeSpan.Zero,
                    "Интервал опроса уставок должен быть больше нуля.");

            services.TryAddSingleton<IMvkSetpointStore>(provider =>
            {
                Mvk01AlarmOptions options = provider
                    .GetRequiredService<IOptions<Mvk01AlarmOptions>>()
                    .Value;

                return new JsonMvkSetpointStore(options.SetpointsFile);
            });

            services.TryAddSingleton<Mvk01AlarmService>(provider =>
            {
                Mvk01AlarmOptions options = provider
                    .GetRequiredService<IOptions<Mvk01AlarmOptions>>()
                    .Value;

                return new Mvk01AlarmService(
                    provider.GetRequiredService<IMvk01DeviceFactory>(),
                    provider.GetRequiredService<IMvkSetpointStore>(),
                    options,
                    provider.GetService<ILogger<Mvk01AlarmService>>());
            });

            services.TryAddSingleton<IMvk01AlarmService>(provider =>
                provider.GetRequiredService<Mvk01AlarmService>());

            return services;
        }
    }
}
