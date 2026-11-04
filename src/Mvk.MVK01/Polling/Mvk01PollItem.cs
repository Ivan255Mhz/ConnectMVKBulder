using System;

namespace Mvk.MVK01.Polling
{
    /// <summary>
    /// Параметр, который наблюдается через <see cref="Mvk01Watcher"/>.
    /// </summary>
    public sealed record Mvk01PollItem
    {
        /// <summary>
        /// Создаёт элемент наблюдения.
        /// </summary>
        /// <param name="number">Номер расчётного параметра (1–184).</param>
        /// <param name="channel">
        /// Номер канала (1–8) для удобного доступа к значению через
        /// <see cref="Mvk01Sample.ChannelValue"/>; <see langword="null"/> — все каналы.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Номер параметра или канала вне допустимого диапазона.
        /// </exception>
        public Mvk01PollItem(Mvk01MeasurementNumber number, int? channel = null)
        {
            int value = (int)number;
            if (value < 1 || value > Mvk01RegisterMap.MaxMeasurementNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(number),
                    number,
                    $"Номер параметра должен быть в диапазоне 1..{Mvk01RegisterMap.MaxMeasurementNumber}.");
            }

            if (channel.HasValue && (channel.Value < 1 || channel.Value > Mvk01RegisterMap.MaxChannels))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    $"Номер канала должен быть в диапазоне 1..{Mvk01RegisterMap.MaxChannels}.");
            }

            Number = number;
            Channel = channel;
        }

        /// <summary>
        /// Номер расчётного параметра (1–184).
        /// </summary>
        public Mvk01MeasurementNumber Number { get; }

        /// <summary>
        /// Номер канала для <see cref="Mvk01Sample.ChannelValue"/> либо <see langword="null"/>.
        /// </summary>
        public int? Channel { get; }
    }
}
