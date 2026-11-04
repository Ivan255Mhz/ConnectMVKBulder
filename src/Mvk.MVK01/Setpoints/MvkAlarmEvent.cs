using System;
using Mvk.Modbus;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Событие перехода состояния уставки. Повторные сэмплы с тем же
    /// состоянием событий не создают.
    /// </summary>
    public sealed record MvkAlarmEvent
    {
        /// <summary>Время события (UTC).</summary>
        public required DateTimeOffset Timestamp { get; init; }

        /// <summary>Идентификатор устройства.</summary>
        public required string DeviceId { get; init; }

        /// <summary>Уставка.</summary>
        public required MvkSetpoint Setpoint { get; init; }

        /// <summary>Новое состояние.</summary>
        public required MvkSetpointState State { get; init; }

        /// <summary>Предыдущее состояние.</summary>
        public required MvkSetpointState PreviousState { get; init; }

        /// <summary>Значение в момент события; <see cref="float.NaN"/> при отсутствии данных.</summary>
        public required float Value { get; init; }

        /// <summary>Ошибка получения данных, если событие связано с потерей связи.</summary>
        public MvkModbusException? Error { get; init; }

        /// <summary>Сообщение уставки или сформированный текст.</summary>
        public string? Message { get; init; }

        /// <summary>Тревога активна.</summary>
        public bool IsActive => State == MvkSetpointState.Active;

        /// <summary>Тревога снята.</summary>
        public bool IsClear => State == MvkSetpointState.Normal;

        /// <inheritdoc/>
        public override string ToString()
        {
            string name = Setpoint.Name ?? Setpoint.Id;
            return $"{Timestamp:O} {DeviceId} {name}: {PreviousState} -> {State} ({Value})";
        }
    }
}
