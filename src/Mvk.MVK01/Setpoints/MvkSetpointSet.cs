using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Набор уставок с fluent-построителем.
    /// </summary>
    public sealed class MvkSetpointSet : IReadOnlyCollection<MvkSetpoint>
    {
        private readonly List<MvkSetpoint> setpoints = [];

        /// <summary>
        /// Создаёт пустой набор.
        /// </summary>
        public static MvkSetpointSet Create() => new();

        /// <summary>
        /// Количество уставок.
        /// </summary>
        public int Count => setpoints.Count;

        /// <summary>
        /// Уставки набора.
        /// </summary>
        public IReadOnlyList<MvkSetpoint> Setpoints => setpoints.AsReadOnly();

        /// <summary>
        /// Начинает описание уставок для параметра и канала.
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства.</param>
        /// <param name="measurement">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8).</param>
        public MvkSetpointBuilder Add(
            string deviceId,
            Mvk01MeasurementNumber measurement,
            int channel = 1)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Не задан идентификатор устройства.", nameof(deviceId));
            }

            int number = (int)measurement;
            if (number < 1 || number > Mvk01RegisterMap.MaxMeasurementNumber)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(measurement),
                    measurement,
                    $"Номер параметра должен быть в диапазоне 1..{Mvk01RegisterMap.MaxMeasurementNumber}.");
            }

            if (channel < 1 || channel > Mvk01RegisterMap.MaxChannels)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    channel,
                    $"Номер канала должен быть в диапазоне 1..{Mvk01RegisterMap.MaxChannels}.");
            }

            return new MvkSetpointBuilder(this, deviceId.Trim(), measurement, channel);
        }

        /// <summary>
        /// Добавляет готовую уставку.
        /// </summary>
        /// <param name="setpoint">Уставка.</param>
        public MvkSetpointSet Add(MvkSetpoint setpoint)
        {
            ArgumentNullException.ThrowIfNull(setpoint);
            setpoints.Add(setpoint);
            return this;
        }

        /// <summary>
        /// Добавляет несколько готовых уставок.
        /// </summary>
        /// <param name="items">Уставки.</param>
        public MvkSetpointSet AddRange(IEnumerable<MvkSetpoint> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            setpoints.AddRange(items);
            return this;
        }

        /// <summary>
        /// Проверяет набор и возвращает список ошибок.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            return MvkSetpointValidator.Validate(setpoints);
        }

        /// <summary>
        /// Проверяет набор и выбрасывает <see cref="MvkSetpointValidationException"/> при ошибках.
        /// </summary>
        public void ValidateAndThrow()
        {
            MvkSetpointValidator.ThrowIfInvalid(setpoints);
        }

        /// <inheritdoc/>
        public IEnumerator<MvkSetpoint> GetEnumerator()
        {
            return setpoints.GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        internal string CreateId(
            string deviceId,
            Mvk01MeasurementNumber measurement,
            int channel,
            MvkAlarmSeverity severity,
            MvkSetpointComparison comparison)
        {
            string baseId = $"{deviceId}/ch{channel}/{measurement}/{severity}/{comparison}";
            string id = baseId;
            int suffix = 2;

            while (setpoints.Any(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                id = $"{baseId}-{suffix++}";
            }

            return id;
        }
    }
}
