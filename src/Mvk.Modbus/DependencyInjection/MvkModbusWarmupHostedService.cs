using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Pool;

namespace Mvk.Modbus.DependencyInjection
{
    /// <summary>
    /// При старте хоста инициализирует пул устройств (проверка конфигурации) и логирует состав пула.
    /// </summary>
    public sealed class MvkModbusWarmupHostedService : IHostedService
    {
        private readonly IMvkDevicePool pool;
        private readonly ILogger<MvkModbusWarmupHostedService> logger;

        /// <summary>
        /// Создаёт hosted-сервис.
        /// </summary>
        /// <param name="pool">Пул устройств.</param>
        /// <param name="logger">Логгер.</param>
        public MvkModbusWarmupHostedService(
            IMvkDevicePool pool,
            ILogger<MvkModbusWarmupHostedService> logger)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation(
                "Пул МВК инициализирован: устройств {DeviceCount} ({Devices}).",
                pool.Devices.Count,
                string.Join(", ", pool.Devices.Select(device => device.Settings.Id)));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
