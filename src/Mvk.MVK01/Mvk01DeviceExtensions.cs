using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.MVK01.Polling;

namespace Mvk.MVK01
{
    /// <summary>
    /// Компактные операции над <see cref="IMvk01Device"/>: чтение одного значения
    /// и периодическое наблюдение без ручного создания watcher'а.
    /// </summary>
    public static class Mvk01DeviceExtensions
    {
        /// <summary>
        /// Читает значение одного канала параметра.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static async Task<float> ReadValueAsync(
            this IMvk01Device device,
            Mvk01MeasurementNumber number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(device);
            ValidateChannel(channel);

            float[] values = await device
                .ReadMeasurementByNumberAsync(number, cancellationToken)
                .ConfigureAwait(false);

            return values[channel - 1];
        }

        /// <summary>
        /// Читает значение одного канала параметра по числовому номеру.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static Task<float> ReadValueAsync(
            this IMvk01Device device,
            int number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValueAsync(
                (Mvk01MeasurementNumber)number,
                channel,
                cancellationToken);
        }

        /// <summary>
        /// Читает значения параметра по всем 8 каналам.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static Task<float[]> ReadValuesAsync(
            this IMvk01Device device,
            Mvk01MeasurementNumber number,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(device);
            return device.ReadMeasurementByNumberAsync(number, cancellationToken);
        }

        /// <summary>
        /// Читает значения параметра по всем 8 каналам по числовому номеру.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static Task<float[]> ReadValuesAsync(
            this IMvk01Device device,
            int number,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValuesAsync(
                (Mvk01MeasurementNumber)number,
                cancellationToken);
        }

        /// <summary>
        /// Наблюдает за одним параметром и каналом.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            Mvk01MeasurementNumber number,
            int channel = 1,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(device);

            return CreateWatcher(device, interval)
                .WatchAsync(new Mvk01PollItem(number, channel), cancellationToken);
        }

        /// <summary>
        /// Наблюдает за одним параметром и каналом по числовому номеру.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            int number,
            int channel = 1,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            return device.WatchAsync(
                (Mvk01MeasurementNumber)number,
                channel,
                interval,
                cancellationToken);
        }

        /// <summary>Короткая форма наблюдения с токеном отмены.</summary>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            Mvk01MeasurementNumber number,
            int channel,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(number, channel, interval: null, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения по числовому номеру с токеном отмены.</summary>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            int number,
            int channel,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(number, channel, interval: null, cancellationToken);
        }

        /// <summary>
        /// Наблюдает за готовым элементом опроса.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="item">Наблюдаемый параметр.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            Mvk01PollItem item,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(item);

            return CreateWatcher(device, interval).WatchAsync(item, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения за элементом с токеном отмены.</summary>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            Mvk01PollItem item,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(item, interval: null, cancellationToken);
        }

        /// <summary>
        /// Наблюдает за набором параметров.
        /// </summary>
        /// <param name="device">Типизированное устройство.</param>
        /// <param name="items">Наблюдаемые параметры.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            IReadOnlyList<Mvk01PollItem> items,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(items);

            return CreateWatcher(device, interval).WatchAsync(items, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения за набором с токеном отмены.</summary>
        public static IAsyncEnumerable<Mvk01Sample> WatchAsync(
            this IMvk01Device device,
            IReadOnlyList<Mvk01PollItem> items,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(items, interval: null, cancellationToken);
        }

        private static Mvk01Watcher CreateWatcher(IMvk01Device device, TimeSpan? interval)
        {
            return interval is null
                ? new Mvk01Watcher(device)
                : new Mvk01Watcher(device, interval.Value);
        }

        private static void ValidateChannel(int channel)
        {
            if (channel < 1 || channel > Mvk01RegisterMap.MaxChannels)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    $"Номер канала должен быть в диапазоне 1..{Mvk01RegisterMap.MaxChannels}.");
            }
        }
    }
}
