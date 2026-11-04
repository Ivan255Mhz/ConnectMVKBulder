using System;
using Mvk.Modbus;

namespace Mvk.MVK01.Polling
{
    /// <summary>
    /// Один результат опроса в <see cref="Mvk01Watcher"/>: значения параметра
    /// по всем каналам либо ошибка обращения к устройству.
    /// </summary>
    public sealed record Mvk01Sample
    {
        /// <summary>
        /// Время получения результата (UTC).
        /// </summary>
        public required DateTimeOffset Timestamp { get; init; }

        /// <summary>
        /// Наблюдаемый параметр.
        /// </summary>
        public required Mvk01PollItem Item { get; init; }

        /// <summary>
        /// Значения по всем 8 каналам. При ошибке — пустой массив.
        /// </summary>
        public required float[] Values { get; init; }

        /// <summary>
        /// Ошибка обращения к устройству либо <see langword="null"/> при успехе.
        /// </summary>
        public MvkModbusException? Error { get; init; }

        /// <summary>
        /// Значения получены без ошибки.
        /// </summary>
        public bool IsSuccess => Error is null;

        /// <summary>
        /// Значение выбранного канала (<see cref="Mvk01PollItem.Channel"/>)
        /// либо <see langword="null"/>, если канал не задан или данные не получены.
        /// </summary>
        public float? ChannelValue
        {
            get
            {
                if (Item.Channel is not int channel || Values.Length < channel)
                {
                    return null;
                }

                return Values[channel - 1];
            }
        }
    }
}
