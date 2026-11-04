using System;
using Mvk.Modbus;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Текущее состояние одной уставки.
    /// </summary>
    public sealed record MvkSetpointStatus
    {
        /// <summary>Уставка.</summary>
        public required MvkSetpoint Setpoint { get; init; }

        /// <summary>Текущее состояние.</summary>
        public required MvkSetpointState State { get; init; }

        /// <summary>Последнее полученное значение; <see cref="float.NaN"/> при отсутствии данных.</summary>
        public required float LastValue { get; init; }

        /// <summary>Время последнего обновления (UTC).</summary>
        public required DateTimeOffset Timestamp { get; init; }

        /// <summary>Время начала активной тревоги.</summary>
        public DateTimeOffset? ActiveSince { get; init; }

        /// <summary>Ошибка получения данных, если состояние <see cref="MvkSetpointState.Unknown"/>.</summary>
        public MvkModbusException? Error { get; init; }

        /// <summary>Тревога активна.</summary>
        public bool IsActive => State == MvkSetpointState.Active;
    }
}
