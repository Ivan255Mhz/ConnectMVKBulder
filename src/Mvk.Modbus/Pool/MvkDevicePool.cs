using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Transport;

namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Пул устройств: конструкции устройств из конфигурации, доступ по идентификатору,
    /// фоновый реконнект и отключение простаивающих соединений.
    /// </summary>
    public sealed class MvkDevicePool : IMvkDevicePool, IDisposable
    {
        private readonly ConcurrentDictionary<string, MvkDevice> devices =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly IMvkTransportFactory transportFactory;
        private readonly IReadOnlyList<IModbusMiddleware> middlewares;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<MvkDevicePool> logger;
        private readonly CancellationTokenSource maintenanceCancellation = new();

        private Task? maintenanceTask;
        private bool disposed;

        /// <summary>
        /// Создаёт пул устройств из конфигурации.
        /// </summary>
        /// <param name="options">Секция конфигурации MvkModbus.</param>
        /// <param name="transportFactory">
        /// Фабрика транспортов; по умолчанию <see cref="NModbusTransportFactory"/>.
        /// </param>
        /// <param name="middlewares">
        /// Middleware пайплайна; по умолчанию набор <see cref="MvkPipelineFactory.CreateDefaultMiddlewares"/>.
        /// </param>
        /// <param name="loggerFactory">Фабрика логгеров (необязательна).</param>
        public MvkDevicePool(
            MvkModbusOptions options,
            IMvkTransportFactory? transportFactory = null,
            IEnumerable<IModbusMiddleware>? middlewares = null,
            ILoggerFactory? loggerFactory = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            logger = this.loggerFactory.CreateLogger<MvkDevicePool>();

            this.transportFactory = transportFactory ?? new NModbusTransportFactory(this.loggerFactory);
            this.middlewares = middlewares?.ToList() ?? MvkPipelineFactory.CreateDefaultMiddlewares(this.loggerFactory);

            foreach (MvkDeviceSettings settings in options.ResolveDevices())
            {
                AddCore(settings);
            }

            if (options.Pool.ReconnectEnabled)
            {
                maintenanceTask = Task.Run(() => MaintenanceLoopAsync(maintenanceCancellation.Token));
            }
        }

        /// <inheritdoc/>
        public MvkModbusOptions Options { get; }

        /// <inheritdoc/>
        public IReadOnlyCollection<IMvkDevice> Devices =>
            devices.Values.Cast<IMvkDevice>().ToList();

        /// <inheritdoc/>
        public event EventHandler<MvkDeviceStateChangedEventArgs>? DeviceStateChanged;

        /// <summary>
        /// Создаёт пул из JSON-файла (секция <c>MvkModbus</c>).
        /// </summary>
        /// <param name="jsonFilePath">Путь к JSON-файлу.</param>
        /// <param name="loggerFactory">Фабрика логгеров (необязательна).</param>
        public static MvkDevicePool FromJsonFile(string jsonFilePath, ILoggerFactory? loggerFactory = null)
        {
            MvkModbusOptions options = MvkModbusOptionsLoader.LoadFromJsonFile(jsonFilePath);
            return new MvkDevicePool(options, loggerFactory: loggerFactory);
        }

        /// <inheritdoc/>
        public IMvkDevice GetDevice(string id)
        {
            if (TryGetDevice(id, out IMvkDevice device))
            {
                return device;
            }

            throw new MvkModbusException(
                $"Устройство '{id}' не найдено в конфигурации пула.",
                MvkErrorKind.DeviceNotFound,
                id);
        }

        /// <inheritdoc/>
        public bool TryGetDevice(string id, out IMvkDevice device)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Не задан идентификатор устройства.", nameof(id));
            }

            if (devices.TryGetValue(id.Trim(), out MvkDevice? found))
            {
                device = found;
                return true;
            }

            device = null!;
            return false;
        }

        /// <inheritdoc/>
        public IMvkDevice AddDevice(MvkDeviceOptions deviceOptions)
        {
            ArgumentNullException.ThrowIfNull(deviceOptions);

            MvkDeviceSettings settings = deviceOptions.Resolve(Options.Defaults);

            if (string.IsNullOrWhiteSpace(settings.Id))
            {
                throw new MvkModbusException(
                    "Не задан идентификатор Id добавляемого устройства.",
                    MvkErrorKind.Configuration);
            }

            return AddCore(settings, requireUnique: true);
        }

        /// <inheritdoc/>
        public bool RemoveDevice(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !devices.TryRemove(id.Trim(), out MvkDevice? device))
            {
                return false;
            }

            device.StateChanged -= OnDeviceStateChanged;
            device.Dispose();
            return true;
        }

        /// <inheritdoc/>
        public async Task MaintainAsync(CancellationToken cancellationToken = default)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            foreach (MvkDevice device in devices.Values.ToList())
            {
                await device.MaintainAsync(now, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            await maintenanceCancellation.CancelAsync().ConfigureAwait(false);

            if (maintenanceTask is not null)
            {
                try
                {
                    await maintenanceTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            foreach (MvkDevice device in devices.Values)
            {
                device.StateChanged -= OnDeviceStateChanged;
                device.Dispose();
            }

            devices.Clear();
            maintenanceCancellation.Dispose();
        }

        /// <summary>
        /// Синхронно освобождает ресурсы пула (для контейнеров, поддерживающих только
        /// синхронное освобождение служб).
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private MvkDevice AddCore(MvkDeviceSettings settings, bool requireUnique = false)
        {
            if (!requireUnique && devices.ContainsKey(settings.Id))
            {
                return devices[settings.Id];
            }

            IMvkTransport transport = transportFactory.Create(settings);
            MvkPipeline pipeline = new(
                MvkPipeline.TerminalFromTransport(transport),
                middlewares);

            MvkDevice device = new(
                settings,
                transport,
                pipeline,
                loggerFactory.CreateLogger<MvkDevice>());

            if (!devices.TryAdd(settings.Id, device))
            {
                device.Dispose();

                if (requireUnique)
                {
                    throw new MvkModbusException(
                        $"Устройство '{settings.Id}' уже существует в пуле.",
                        MvkErrorKind.Configuration,
                        settings.Id);
                }

                return devices[settings.Id];
            }

            device.StateChanged += OnDeviceStateChanged;

            logger.LogDebug(
                "МВК '{DeviceId}': устройство добавлено в пул [{Endpoint}], Enabled={Enabled}.",
                settings.Id,
                settings.Endpoint,
                settings.Enabled);

            return device;
        }

        private void OnDeviceStateChanged(object? sender, MvkDeviceStateChangedEventArgs args)
        {
            DeviceStateChanged?.Invoke(this, args);
        }

        private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
        {
            int intervalSeconds = Math.Max(1, Options.Pool.HealthCheckIntervalSeconds);

            using PeriodicTimer timer = new(TimeSpan.FromSeconds(intervalSeconds));

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        await MaintainAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Ошибка обслуживания пула устройств МВК.");
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
