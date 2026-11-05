using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;

namespace Mvk.MVK01.Polling
{
    /// <summary>
    /// Наблюдение за параметрами МВК01: периодически читает выбранные параметры
    /// и возвращает результаты через <see cref="IAsyncEnumerable{T}"/>.
    /// Ошибки обращения передаются в <see cref="Mvk01Sample.Error"/>, поток продолжается.
    /// </summary>
    public sealed class Mvk01Watcher
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Создаёт наблюдатель с интервалом по умолчанию 500 мс.
        /// </summary>
        /// <param name="device">Типизированное устройство МВК01.</param>
        public Mvk01Watcher(IMvk01Device device)
            : this(device, DefaultInterval)
        {
        }

        /// <summary>
        /// Создаёт наблюдатель с интервалом по умолчанию 500 мс и заданными настройками.
        /// </summary>
        /// <param name="device">Типизированное устройство МВК01.</param>
        /// <param name="options">Настройки батчинга параметров.</param>
        public Mvk01Watcher(IMvk01Device device, Mvk01SnapshotOptions options)
            : this(device, DefaultInterval, options)
        {
        }

        /// <summary>
        /// Создаёт наблюдатель.
        /// </summary>
        /// <param name="device">Типизированное устройство МВК01.</param>
        /// <param name="interval">Интервал между циклами опроса (больше нуля).</param>
        /// <param name="options">Настройки батчинга параметров; по умолчанию до 7 блоков на запрос.</param>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> равен <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> не больше нуля.</exception>
        public Mvk01Watcher(
            IMvk01Device device,
            TimeSpan interval,
            Mvk01SnapshotOptions? options = null)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval),
                    interval,
                    "Интервал наблюдения должен быть больше нуля.");
            }

            Interval = interval;
            Options = options ?? new Mvk01SnapshotOptions();
        }

        /// <summary>
        /// Наблюдаемое устройство.
        /// </summary>
        public IMvk01Device Device { get; }

        /// <summary>
        /// Интервал между циклами опроса.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Настройки батчинга параметров.
        /// </summary>
        public Mvk01SnapshotOptions Options { get; }

        /// <summary>
        /// Наблюдает за одним параметром и каналом.
        /// </summary>
        /// <param name="number">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            Mvk01MeasurementNumber number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            return WatchAsync(new Mvk01PollItem(number, channel), cancellationToken);
        }

        /// <summary>
        /// Наблюдает за одним параметром и каналом по числовому номеру.
        /// </summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            int number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            return WatchAsync(
                new Mvk01PollItem((Mvk01MeasurementNumber)number, channel),
                cancellationToken);
        }

        /// <summary>
        /// Наблюдает за одним параметром.
        /// </summary>
        /// <param name="item">Наблюдаемый параметр.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> равен <see langword="null"/>.</exception>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            Mvk01PollItem item,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);
            return WatchAsync([item], cancellationToken);
        }

        /// <summary>
        /// Наблюдает за набором параметров. Соседние параметры объединяются
        /// в один запрос (до 7 блоков по 16 регистров); результаты возвращаются
        /// по мере готовности в исходном порядке.
        /// </summary>
        /// <param name="items">Список наблюдаемых параметров (не пустой).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="ArgumentNullException"><paramref name="items"/> равен <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Список пуст или содержит <see langword="null"/>.</exception>
        public async IAsyncEnumerable<Mvk01Sample> WatchAsync(
            IReadOnlyList<Mvk01PollItem> items,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(items);

            if (items.Count == 0)
            {
                throw new ArgumentException("Список параметров для наблюдения пуст.", nameof(items));
            }

            Mvk01PollItem[] snapshot = new Mvk01PollItem[items.Count];
            for (int index = 0; index < items.Count; index++)
            {
                snapshot[index] = items[index]
                    ?? throw new ArgumentException("Список параметров содержит null.", nameof(items));
            }

            IReadOnlyList<Mvk01PollBatch> batches = Mvk01ReadPlanner.CreatePollBatches(
                snapshot,
                Options.MaxMeasurementBatchBlocks);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long startedAt = Stopwatch.GetTimestamp();

                foreach (Mvk01PollBatch batch in batches)
                {
                    ushort[]? registers = null;
                    MvkModbusException? error = null;
                    Mvk01ReadChunk range = Mvk01ReadPlanner.MeasurementRangeChunk(
                        batch.StartNumber,
                        batch.EndNumber);

                    try
                    {
                        registers = await Device.Device
                            .ReadHoldingRegistersAsync(range.Address, range.Quantity, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Canceled)
                    {
                        throw new OperationCanceledException(ex.Message, ex, cancellationToken);
                    }
                    catch (MvkModbusException ex)
                    {
                        error = ex;
                    }

                    DateTimeOffset timestamp = DateTimeOffset.UtcNow;

                    foreach (int itemIndex in batch.Indexes)
                    {
                        Mvk01PollItem item = snapshot[itemIndex];

                        if (error is not null)
                        {
                            yield return new Mvk01Sample
                            {
                                Timestamp = timestamp,
                                DeviceId = Device.Id,
                                Item = item,
                                Values = Array.Empty<float>(),
                                Error = error
                            };
                            continue;
                        }

                        int offset = ((int)item.Number - batch.StartNumber)
                            * Mvk01RegisterMap.MeasurementByNumberStride;
                        float[] values = Mvk01Codec.DecodeFloats(
                            registers!.AsSpan(offset, Mvk01RegisterMap.MeasurementByNumberRegisterCount),
                            Mvk01RegisterMap.MeasurementByNumberChannels,
                            Device.Settings.ByteOrder);

                        yield return new Mvk01Sample
                        {
                            Timestamp = timestamp,
                            DeviceId = Device.Id,
                            Item = item,
                            Values = values
                        };
                    }
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);
                TimeSpan delay = Interval - elapsed;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
