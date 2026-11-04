using System;
using System.Collections.Generic;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Сравнение значения с уставкой.
    /// </summary>
    public enum MvkSetpointComparison
    {
        /// <summary>Значение выше порога.</summary>
        Above,

        /// <summary>Значение ниже порога.</summary>
        Below,

        /// <summary>Значение вне диапазона [Threshold; UpperThreshold].</summary>
        Outside,

        /// <summary>Значение внутри диапазона [Threshold; UpperThreshold].</summary>
        Inside
    }

    /// <summary>
    /// Важность уставки. Несколько уставок на один параметр позволяют
    /// сделать уровни Warning/Alarm/Trip.
    /// </summary>
    public enum MvkAlarmSeverity
    {
        /// <summary>Информационное событие.</summary>
        Info,

        /// <summary>Предупреждение.</summary>
        Warning,

        /// <summary>Авария.</summary>
        Alarm,

        /// <summary>Аварийный останов / защита.</summary>
        Trip
    }

    /// <summary>
    /// Состояние уставки.
    /// </summary>
    public enum MvkSetpointState
    {
        /// <summary>Значение в норме.</summary>
        Normal,

        /// <summary>Порог превышен, но выдерживается задержка срабатывания.</summary>
        Pending,

        /// <summary>Тревога активна.</summary>
        Active,

        /// <summary>Нет достоверных данных: ошибка связи или нечисловое значение.</summary>
        Unknown
    }

    /// <summary>
    /// Прикладная уставка МВК01. Хранится в приложении, а не в регистрах устройства.
    /// </summary>
    public sealed record MvkSetpoint
    {
        /// <summary>Уникальный идентификатор уставки.</summary>
        public required string Id { get; init; }

        /// <summary>Идентификатор устройства из конфигурации пула.</summary>
        public required string DeviceId { get; init; }

        /// <summary>Номер расчётного параметра (1–184).</summary>
        public required Mvk01MeasurementNumber Measurement { get; init; }

        /// <summary>Номер канала (1–8).</summary>
        public required int Channel { get; init; }

        /// <summary>Правило сравнения.</summary>
        public required MvkSetpointComparison Comparison { get; init; }

        /// <summary>Порог (нижняя граница для диапазона).</summary>
        public required double Threshold { get; init; }

        /// <summary>Верхняя граница для <see cref="MvkSetpointComparison.Outside"/> и <see cref="MvkSetpointComparison.Inside"/>.</summary>
        public double? UpperThreshold { get; init; }

        /// <summary>
        /// Гистерезис в единицах измерения: тревога снимается только после
        /// возврата значения за порог на эту величину.
        /// </summary>
        public double Hysteresis { get; init; }

        /// <summary>Задержка, в течение которой условие должно удерживаться до срабатывания.</summary>
        public TimeSpan ActivationDelay { get; init; }

        /// <summary>Важность уставки.</summary>
        public MvkAlarmSeverity Severity { get; init; } = MvkAlarmSeverity.Warning;

        /// <summary>Уставка включена.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>Отображаемое имя.</summary>
        public string? Name { get; init; }

        /// <summary>Сообщение для журнала/уведомления.</summary>
        public string? Message { get; init; }

        /// <summary>Произвольные метки.</summary>
        public IReadOnlyDictionary<string, string>? Tags { get; init; }
    }
}
