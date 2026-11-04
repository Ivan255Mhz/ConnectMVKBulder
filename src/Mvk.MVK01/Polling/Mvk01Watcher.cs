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
        /// Создаёт наблюдатель.
        /// </summary>
        /// <param name="device">Типизированное устройство МВК01.</param>
        /// <param name="interval">Интервал между циклами опроса (больше нуля).</param>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> равен <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> не больше нуля.</exception>
        public Mvk01Watcher(IMvk01Device device, TimeSpan interval)
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
        /// Наблюдает за набором параметров. За один цикл выполняется по одному
        /// запросу на каждый параметр; результаты возвращаются по мере готовности.
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

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long startedAt = Stopwatch.GetTimestamp();

                for (int index = 0; index < snapshot.Length; index++)
                {
                    Mvk01PollItem item = snapshot[index];

                    Mvk01Sample sample;

                    try
                    {
                        float[] values = await Device
                            .ReadMeasurementByNumberAsync(item.Number, cancellationToken)
                            .ConfigureAwait(false);

                        sample = new Mvk01Sample
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            DeviceId = Device.Id,
                            Item = item,
                            Values = values
                        };
                    }
                    catch (MvkModbusException ex) when (ex.ErrorKind == MvkErrorKind.Canceled)
                    {
                        throw new OperationCanceledException(ex.Message, ex, cancellationToken);
                    }
                    catch (MvkModbusException ex)
                    {
                        sample = new Mvk01Sample
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            DeviceId = Device.Id,
                            Item = item,
                            Values = Array.Empty<float>(),
                            Error = ex
                        };
                    }

                    yield return sample;
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
