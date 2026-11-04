using System;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Fluent-построитель уставок для одного параметра и канала.
    /// </summary>
    public sealed class MvkSetpointBuilder
    {
        private readonly MvkSetpointSet owner;
        private readonly string deviceId;
        private readonly Mvk01MeasurementNumber measurement;
        private readonly int channel;

        internal MvkSetpointBuilder(
            MvkSetpointSet owner,
            string deviceId,
            Mvk01MeasurementNumber measurement,
            int channel)
        {
            this.owner = owner;
            this.deviceId = deviceId;
            this.measurement = measurement;
            this.channel = channel;
        }

        /// <summary>
        /// Позволяет завершить fluent-цепочку присваиванием набору:
        /// <c>MvkSetpointSet set = MvkSetpointSet.Create().Add(...).Warning(...);</c>
        /// </summary>
        /// <param name="builder">Построитель.</param>
        public static implicit operator MvkSetpointSet(MvkSetpointBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.owner;
        }

        /// <summary>Добавляет предупреждение «выше порога».</summary>
        public MvkSetpointBuilder Warning(
            double threshold,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Above(threshold, MvkAlarmSeverity.Warning, hysteresis, activationDelay, name, id);
        }

        /// <summary>Добавляет аварию «выше порога».</summary>
        public MvkSetpointBuilder Alarm(
            double threshold,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Above(threshold, MvkAlarmSeverity.Alarm, hysteresis, activationDelay, name, id);
        }

        /// <summary>Добавляет аварийный останов «выше порога».</summary>
        public MvkSetpointBuilder Trip(
            double threshold,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Above(threshold, MvkAlarmSeverity.Trip, hysteresis, activationDelay, name, id);
        }

        /// <summary>Добавляет информационную уставку «выше порога».</summary>
        public MvkSetpointBuilder Info(
            double threshold,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Above(threshold, MvkAlarmSeverity.Info, hysteresis, activationDelay, name, id);
        }

        /// <summary>Добавляет уставку «выше порога».</summary>
        public MvkSetpointBuilder Above(
            double threshold,
            MvkAlarmSeverity severity = MvkAlarmSeverity.Warning,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Add(
                MvkSetpointComparison.Above,
                threshold,
                null,
                severity,
                hysteresis,
                activationDelay,
                name,
                id);
        }

        /// <summary>Добавляет уставку «ниже порога».</summary>
        public MvkSetpointBuilder Below(
            double threshold,
            MvkAlarmSeverity severity = MvkAlarmSeverity.Warning,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Add(
                MvkSetpointComparison.Below,
                threshold,
                null,
                severity,
                hysteresis,
                activationDelay,
                name,
                id);
        }

        /// <summary>Добавляет уставку «вне диапазона».</summary>
        public MvkSetpointBuilder Outside(
            double lower,
            double upper,
            MvkAlarmSeverity severity = MvkAlarmSeverity.Warning,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Add(
                MvkSetpointComparison.Outside,
                lower,
                upper,
                severity,
                hysteresis,
                activationDelay,
                name,
                id);
        }

        /// <summary>Добавляет уставку «внутри диапазона».</summary>
        public MvkSetpointBuilder Inside(
            double lower,
            double upper,
            MvkAlarmSeverity severity = MvkAlarmSeverity.Warning,
            double hysteresis = 0,
            TimeSpan? activationDelay = null,
            string? name = null,
            string? id = null)
        {
            return Add(
                MvkSetpointComparison.Inside,
                lower,
                upper,
                severity,
                hysteresis,
                activationDelay,
                name,
                id);
        }

        private MvkSetpointBuilder Add(
            MvkSetpointComparison comparison,
            double threshold,
            double? upperThreshold,
            MvkAlarmSeverity severity,
            double hysteresis,
            TimeSpan? activationDelay,
            string? name,
            string? id)
        {
            string finalId = string.IsNullOrWhiteSpace(id)
                ? owner.CreateId(deviceId, measurement, channel, severity, comparison)
                : id.Trim();

            owner.Add(new MvkSetpoint
            {
                Id = finalId,
                DeviceId = deviceId,
                Measurement = measurement,
                Channel = channel,
                Comparison = comparison,
                Threshold = threshold,
                UpperThreshold = upperThreshold,
                Hysteresis = hysteresis,
                ActivationDelay = activationDelay ?? TimeSpan.Zero,
                Severity = severity,
                Name = name
            });

            return this;
        }
    }
}
