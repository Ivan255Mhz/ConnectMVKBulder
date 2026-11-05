using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Pool;
using Mvk.MVK01.Polling;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Высокоуровневый сервис уставок: загружает уставки, опрашивает устройства
    /// и отдаёт события переходов. Может владеть собственным пулом (сценарий
    /// <c>StartAsync</c>) или работать поверх готовой фабрики устройств.
    /// </summary>
    public sealed class Mvk01AlarmService : IMvk01AlarmService
    {
        private readonly IMvk01DeviceFactory factory;
        private readonly IMvkSetpointStore store;
        private readonly Mvk01AlarmOptions options;
        private readonly ILogger<Mvk01AlarmService> logger;
        private readonly MvkDevicePool? ownedPool;
        private readonly CancellationTokenSource lifetime = new();
        private readonly object sync = new();

        private MvkSetpointEngine? engine;
        private int activeWatch;
        private bool disposed;

        /// <summary>
        /// Создаёт сервис поверх готовой фабрики устройств.
        /// </summary>
        /// <param name="factory">Фабрика типизированных устройств.</param>
        /// <param name="store">Хранилище уставок.</param>
        /// <param name="options">Настройки сервиса.</param>
        /// <param name="logger">Логгер (необязателен).</param>
        public Mvk01AlarmService(
            IMvk01DeviceFactory factory,
            IMvkSetpointStore store,
            Mvk01AlarmOptions? options = null,
            ILogger<Mvk01AlarmService>? logger = null)
            : this(factory, store, options, logger, ownedPool: null)
        {
        }

        private Mvk01AlarmService(
            IMvk01DeviceFactory factory,
            IMvkSetpointStore store,
            Mvk01AlarmOptions? options,
            ILogger<Mvk01AlarmService>? logger,
            MvkDevicePool? ownedPool)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.options = options ?? new Mvk01AlarmOptions();
            this.logger = logger ?? NullLogger<Mvk01AlarmService>.Instance;
            this.ownedPool = ownedPool;

            if (this.options.PollInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    this.options.PollInterval,
                    "Интервал опроса должен быть больше нуля.");
            }
        }

        /// <summary>
        /// Запускает сервис по JSON-конфигурации устройств и JSON-файлу уставок.
        /// Сервис создаёт и освобождает собственный пул.
        /// </summary>
        /// <param name="devicesConfigPath">Путь к конфигурации MvkModbus.</param>
        /// <param name="setpointsPath">Путь к JSON-файлу уставок.</param>
        /// <param name="options">Настройки сервиса.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static Task<Mvk01AlarmService> StartAsync(
            string devicesConfigPath,
            string setpointsPath,
            Mvk01AlarmOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(setpointsPath))
            {
                throw new ArgumentException("Не задан путь к файлу уставок.", nameof(setpointsPath));
            }

            return StartAsync(
                devicesConfigPath,
                new JsonMvkSetpointStore(setpointsPath),
                options,
                cancellationToken);
        }

        /// <summary>
        /// Запускает сервис по JSON-конфигурации устройств и готовому набору
        /// fluent-уставок.
        /// </summary>
        /// <param name="devicesConfigPath">Путь к конфигурации MvkModbus.</param>
        /// <param name="setpoints">Набор уставок.</param>
        /// <param name="options">Настройки сервиса.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static Task<Mvk01AlarmService> StartAsync(
            string devicesConfigPath,
            MvkSetpointSet setpoints,
            Mvk01AlarmOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(setpoints);
            setpoints.ValidateAndThrow();

            return StartAsync(
                devicesConfigPath,
                new InMemoryMvkSetpointStore(setpoints),
                options,
                cancellationToken);
        }

        /// <summary>
        /// Запускает сервис поверх произвольного хранилища уставок.
        /// Сервис создаёт и освобождает собственный пул.
        /// </summary>
        /// <param name="devicesConfigPath">Путь к конфигурации MvkModbus.</param>
        /// <param name="store">Хранилище уставок.</param>
        /// <param name="options">Настройки сервиса.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task<Mvk01AlarmService> StartAsync(
            string devicesConfigPath,
            IMvkSetpointStore store,
            Mvk01AlarmOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(devicesConfigPath))
            {
                throw new ArgumentException("Не задан путь к конфигурации устройств.", nameof(devicesConfigPath));
            }

            ArgumentNullException.ThrowIfNull(store);

            MvkDevicePool pool = MvkDevicePool.FromJsonFile(devicesConfigPath);

            try
            {
                Mvk01DeviceFactory deviceFactory = new(pool);
                Mvk01AlarmService service = new(deviceFactory, store, options, logger: null, ownedPool: pool);

                await service.ReloadSetpointsAsync(cancellationToken).ConfigureAwait(false);
                return service;
            }
            catch
            {
                await pool.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<MvkSetpointStatus> Statuses
        {
            get
            {
                MvkSetpointEngine? current = Volatile.Read(ref engine);
                return current?.Statuses ?? Array.Empty<MvkSetpointStatus>();
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<MvkAlarmEvent> WatchAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (Interlocked.Exchange(ref activeWatch, 1) != 0)
            {
                throw new InvalidOperationException(
                    "Наблюдение уже запущено. Дождитесь завершения предыдущего.");
            }

            using CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, lifetime.Token);

            try
            {
                MvkSetpointEngine currentEngine = await EnsureEngineAsync(linked.Token)
                    .ConfigureAwait(false);

                IGrouping<string, MvkSetpoint>[] groups = currentEngine.Setpoints
                    .Where(setpoint => setpoint.Enabled)
                    .GroupBy(
                        setpoint => setpoint.DeviceId.Trim(),
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (groups.Length > 0)
                {
                    IAsyncEnumerable<Mvk01Sample>[] streams = groups
                        .Select(group => WatchDeviceAsync(
                            group.Key,
                            currentEngine.CreatePollItems(group.Key),
                            linked.Token))
                        .ToArray();

                    await foreach (Mvk01Sample sample in MergeAsync(streams, linked.Token)
                        .ConfigureAwait(false))
                    {
                        foreach (MvkAlarmEvent alarm in currentEngine.Evaluate(
                            sample,
                            sample.Timestamp))
                        {
                            yield return alarm;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref activeWatch, 0);
            }
        }

        /// <inheritdoc/>
        public async Task RunAsync(
            Func<MvkAlarmEvent, CancellationToken, ValueTask> onAlarm,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(onAlarm);

            await foreach (MvkAlarmEvent alarm in WatchAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                try
                {
                    await onAlarm(alarm, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Ошибка обработчика тревоги уставки '{SetpointId}'.",
                        alarm.Setpoint.Id);

                    if (options.OnReactionError is not null)
                    {
                        try
                        {
                            options.OnReactionError(exception, alarm);
                        }
                        catch (Exception handlerException)
                        {
                            logger.LogWarning(
                                handlerException,
                                "Ошибка обработчика OnReactionError уставки '{SetpointId}'.",
                                alarm.Setpoint.Id);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async Task ReloadSetpointsAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (Volatile.Read(ref activeWatch) != 0)
            {
                throw new InvalidOperationException(
                    "Остановите наблюдение перед перезагрузкой уставок.");
            }

            MvkSetpointEngine loaded = await LoadEngineAsync(cancellationToken)
                .ConfigureAwait(false);

            lock (sync)
            {
                engine = loaded;
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
            lifetime.Cancel();

            if (ownedPool is not null)
            {
                await ownedPool.DisposeAsync().ConfigureAwait(false);
            }

            lifetime.Dispose();
        }

        /// <summary>
        /// Синхронно освобождает ресурсы сервиса.
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        private async Task<MvkSetpointEngine> EnsureEngineAsync(
            CancellationToken cancellationToken)
        {
            MvkSetpointEngine? current = Volatile.Read(ref engine);
            if (current is not null)
            {
                return current;
            }

            MvkSetpointEngine loaded = await LoadEngineAsync(cancellationToken)
                .ConfigureAwait(false);

            lock (sync)
            {
                engine ??= loaded;
                return engine;
            }
        }

        private async Task<MvkSetpointEngine> LoadEngineAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MvkSetpoint> setpoints = await store
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);

            MvkSetpointValidator.ThrowIfInvalid(setpoints);
            ValidateDeviceIds(setpoints);

            return new MvkSetpointEngine(setpoints);
        }

        private void ValidateDeviceIds(IReadOnlyList<MvkSetpoint> setpoints)
        {
            HashSet<string> knownDevices = factory.Devices
                .Select(device => device.Settings.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            List<string> failures = [];

            foreach (MvkSetpoint setpoint in setpoints)
            {
                if (!knownDevices.Contains(setpoint.DeviceId.Trim()))
                {
                    failures.Add(
                        $"Уставка '{setpoint.Id}': устройство '{setpoint.DeviceId}' "
                        + "отсутствует в пуле.");
                }
            }

            if (failures.Count > 0)
            {
                throw new MvkSetpointValidationException(failures);
            }
        }

        private async IAsyncEnumerable<Mvk01Sample> WatchDeviceAsync(
            string deviceId,
            IReadOnlyList<Mvk01PollItem> items,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            IMvk01Device device = factory.GetDevice(deviceId);
            Mvk01SnapshotOptions? snapshotOptions = (device as Mvk01Device)?.SnapshotOptions;
            Mvk01Watcher watcher = new(device, options.PollInterval, snapshotOptions);

            await foreach (Mvk01Sample sample in watcher
                .WatchAsync(items, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return sample;
            }
        }

        private static async IAsyncEnumerable<T> MergeAsync<T>(
            IReadOnlyList<IAsyncEnumerable<T>> sources,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource linked = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);

            Channel<T> channel = Channel.CreateBounded<T>(new BoundedChannelOptions(256)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            Task[] producers = sources
                .Select(source => PumpAsync(source, channel.Writer, linked.Token))
                .ToArray();

            _ = CompleteWhenDoneAsync(producers, channel.Writer);

            try
            {
                await foreach (T item in channel.Reader
                    .ReadAllAsync(linked.Token)
                    .ConfigureAwait(false))
                {
                    yield return item;
                }
            }
            finally
            {
                linked.Cancel();
            }
        }

        private static async Task PumpAsync<T>(
            IAsyncEnumerable<T> source,
            ChannelWriter<T> writer,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (T item in source
                    .WithCancellation(cancellationToken)
                    .ConfigureAwait(false))
                {
                    await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                writer.TryComplete(exception);
                throw;
            }
        }

        private static async Task CompleteWhenDoneAsync<T>(
            Task[] producers,
            ChannelWriter<T> writer)
        {
            try
            {
                await Task.WhenAll(producers).ConfigureAwait(false);
                writer.TryComplete();
            }
            catch (Exception exception)
            {
                writer.TryComplete(exception);
            }
        }
    }
}
