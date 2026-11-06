using System;
using System.Collections.Generic;
using System.Linq;
using Mvk.MVK01.Polling;
using Mvk.Modbus;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Оценивает уставки по сэмплам <see cref="Mvk01Sample"/>.
    /// Не зависит от транспорта и может тестироваться без оборудования.
    /// </summary>
    public sealed class MvkSetpointEngine
    {
        private readonly object sync = new();
        private readonly IReadOnlyList<MvkSetpoint> setpoints;
        private readonly Dictionary<string, MvkSetpoint> setpointsById;
        private readonly Dictionary<MeasurementKey, List<MvkSetpoint>> setpointsByMeasurement;
        private readonly Dictionary<string, SetpointState> states;

        /// <summary>
        /// Создаёт движок для набора уставок.
        /// </summary>
        /// <param name="setpoints">Уставки; проверяются на корректность.</param>
        /// <exception cref="MvkSetpointValidationException">Набор содержит ошибки.</exception>
        public MvkSetpointEngine(IEnumerable<MvkSetpoint> setpoints)
        {
            ArgumentNullException.ThrowIfNull(setpoints);

            MvkSetpoint[] items = setpoints.ToArray();
            MvkSetpointValidator.ThrowIfInvalid(items);

            this.setpoints = items;
            setpointsById = new Dictionary<string, MvkSetpoint>(StringComparer.OrdinalIgnoreCase);
            setpointsByMeasurement = new Dictionary<MeasurementKey, List<MvkSetpoint>>();
            states = new Dictionary<string, SetpointState>(StringComparer.OrdinalIgnoreCase);

            foreach (MvkSetpoint setpoint in items)
            {
                string id = setpoint.Id.Trim();
                setpointsById[id] = setpoint;
                states[id] = new SetpointState();

                MeasurementKey key = new(setpoint.DeviceId.Trim(), (int)setpoint.Measurement);
                if (!setpointsByMeasurement.TryGetValue(key, out List<MvkSetpoint>? list))
                {
                    list = [];
                    setpointsByMeasurement[key] = list;
                }

                list.Add(setpoint);
            }
        }

        /// <summary>
        /// Уставки движка.
        /// </summary>
        public IReadOnlyList<MvkSetpoint> Setpoints => setpoints;

        /// <summary>
        /// Снимок текущих состояний всех уставок.
        /// </summary>
        public IReadOnlyList<MvkSetpointStatus> Statuses
        {
            get
            {
                lock (sync)
                {
                    return setpoints.Select(CreateStatus).ToArray();
                }
            }
        }

        /// <summary>
        /// Возвращает состояние уставки по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор уставки.</param>
        public MvkSetpointStatus? GetStatus(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            lock (sync)
            {
                if (!setpointsById.TryGetValue(id.Trim(), out MvkSetpoint? setpoint))
                {
                    return null;
                }

                return CreateStatus(setpoint);
            }
        }

        /// <summary>
        /// Создаёт уникальные элементы опроса для устройства.
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства.</param>
        public IReadOnlyList<Mvk01PollItem> CreatePollItems(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Не задан идентификатор устройства.", nameof(deviceId));
            }

            string id = deviceId.Trim();
            List<Mvk01PollItem> items = [];
            HashSet<int> seenMeasurements = [];

            foreach (MvkSetpoint setpoint in setpoints)
            {
                if (!setpoint.Enabled
                    || !string.Equals(setpoint.DeviceId.Trim(), id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Один запрос MeasurementByNumber возвращает все 8 каналов,
                // поэтому для одного параметра достаточно одного элемента опроса.
                if (seenMeasurements.Add((int)setpoint.Measurement))
                {
                    items.Add(new Mvk01PollItem(setpoint.Measurement, setpoint.Channel));
                }
            }

            return items;
        }

        /// <summary>
        /// Оценивает сэмпл и возвращает события переходов состояний.
        /// Повторные сэмплы с тем же состоянием событий не создают.
        /// </summary>
        /// <param name="sample">Сэмпл с заполненным <see cref="Mvk01Sample.DeviceId"/>.</param>
        /// <param name="now">Время оценки (UTC).</param>
        public IReadOnlyList<MvkAlarmEvent> Evaluate(Mvk01Sample sample, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(sample);

            string deviceId = sample.DeviceId?.Trim() ?? string.Empty;
            if (deviceId.Length == 0)
            {
                throw new InvalidOperationException(
                    "В сэмпле не заполнен DeviceId: движок не может сопоставить уставки с устройством.");
            }

            List<MvkAlarmEvent>? events = null;

            lock (sync)
            {
                MeasurementKey key = new(deviceId, (int)sample.Item.Number);
                if (!setpointsByMeasurement.TryGetValue(key, out List<MvkSetpoint>? matching))
                {
                    return Array.Empty<MvkAlarmEvent>();
                }

                foreach (MvkSetpoint setpoint in matching)
                {
                    if (!setpoint.Enabled)
                    {
                        continue;
                    }

                    SetpointState state = states[setpoint.Id.Trim()];
                    bool hasValue = sample.Error is null
                        && sample.Values.Length >= setpoint.Channel;

                    float value = hasValue ? sample.Values[setpoint.Channel - 1] : float.NaN;

                    if (!hasValue || !float.IsFinite(value))
                    {
                        UpdateUnknown(setpoint, state, now, sample.Error, ref events);
                        continue;
                    }

                    EvaluateValue(setpoint, state, value, now, ref events);
                }
            }

            return events is null ? Array.Empty<MvkAlarmEvent>() : events;
        }

        private void UpdateUnknown(
            MvkSetpoint setpoint,
            SetpointState state,
            DateTimeOffset now,
            MvkModbusException? error,
            ref List<MvkAlarmEvent>? events)
        {
            state.LastValue = float.NaN;
            state.Timestamp = now;
            state.Error = error;
            state.PendingSince = null;

            if (state.State == MvkSetpointState.Unknown)
            {
                return;
            }

            MvkSetpointState previous = state.State;
            state.State = MvkSetpointState.Unknown;
            state.ActiveSince = null;

            AddEvent(ref events, CreateEvent(
                setpoint,
                now,
                MvkSetpointState.Unknown,
                previous,
                float.NaN,
                error));
        }

        private void EvaluateValue(
            MvkSetpoint setpoint,
            SetpointState state,
            float value,
            DateTimeOffset now,
            ref List<MvkAlarmEvent>? events)
        {
            bool condition = IsConditionMet(setpoint, value);

            state.LastValue = value;
            state.Timestamp = now;
            state.Error = null;

            switch (state.State)
            {
                case MvkSetpointState.Normal:
                    if (condition)
                    {
                        StartOrActivate(setpoint, state, value, now, ref events);
                    }

                    break;

                case MvkSetpointState.Pending:
                    if (!condition)
                    {
                        state.State = MvkSetpointState.Normal;
                        state.PendingSince = null;
                    }
                    else if (state.PendingSince is DateTimeOffset pendingSince
                        && now - pendingSince >= setpoint.ActivationDelay)
                    {
                        Activate(setpoint, state, value, now, ref events);
                    }

                    break;

                case MvkSetpointState.Active:
                    if (IsClear(setpoint, value))
                    {
                        MvkSetpointState previous = state.State;
                        state.State = MvkSetpointState.Normal;
                        state.ActiveSince = null;
                        state.PendingSince = null;

                        AddEvent(ref events, CreateEvent(
                            setpoint,
                            now,
                            MvkSetpointState.Normal,
                            previous,
                            value,
                            null));
                    }

                    break;

                case MvkSetpointState.Unknown:
                    if (condition)
                    {
                        StartOrActivate(setpoint, state, value, now, ref events);
                    }
                    else
                    {
                        MvkSetpointState previous = state.State;
                        state.State = MvkSetpointState.Normal;
                        state.ActiveSince = null;
                        state.PendingSince = null;

                        AddEvent(ref events, CreateEvent(
                            setpoint,
                            now,
                            MvkSetpointState.Normal,
                            previous,
                            value,
                            null));
                    }

                    break;
            }
        }

        private static void StartOrActivate(
            MvkSetpoint setpoint,
            SetpointState state,
            float value,
            DateTimeOffset now,
            ref List<MvkAlarmEvent>? events)
        {
            if (setpoint.ActivationDelay <= TimeSpan.Zero)
            {
                Activate(setpoint, state, value, now, ref events);
                return;
            }

            state.State = MvkSetpointState.Pending;
            state.PendingSince = now;
        }

        private static void Activate(
            MvkSetpoint setpoint,
            SetpointState state,
            float value,
            DateTimeOffset now,
            ref List<MvkAlarmEvent>? events)
        {
            MvkSetpointState previous = state.State;
            state.State = MvkSetpointState.Active;
            state.ActiveSince = now;
            state.PendingSince = null;

            AddEvent(ref events, CreateEvent(
                setpoint,
                now,
                MvkSetpointState.Active,
                previous,
                value,
                null));
        }

        private static void AddEvent(ref List<MvkAlarmEvent>? events, MvkAlarmEvent alarm)
        {
            events ??= [];
            events.Add(alarm);
        }

        private static bool IsConditionMet(MvkSetpoint setpoint, float value)
        {
            return setpoint.Comparison switch
            {
                MvkSetpointComparison.Above => value > setpoint.Threshold,
                MvkSetpointComparison.Below => value < setpoint.Threshold,
                MvkSetpointComparison.Outside => value < setpoint.Threshold
                    || value > setpoint.UpperThreshold!.Value,
                MvkSetpointComparison.Inside => value > setpoint.Threshold
                    && value < setpoint.UpperThreshold!.Value,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(setpoint),
                    setpoint.Comparison,
                    "Неизвестное правило сравнения.")
            };
        }

        private static bool IsClear(MvkSetpoint setpoint, float value)
        {
            switch (setpoint.Comparison)
            {
                case MvkSetpointComparison.Above:
                {
                    double clear = setpoint.Threshold - setpoint.Hysteresis;
                    return value <= clear + Tolerance(clear);
                }

                case MvkSetpointComparison.Below:
                {
                    double clear = setpoint.Threshold + setpoint.Hysteresis;
                    return value >= clear - Tolerance(clear);
                }

                case MvkSetpointComparison.Outside:
                {
                    double lower = setpoint.Threshold + setpoint.Hysteresis;
                    double upper = setpoint.UpperThreshold!.Value - setpoint.Hysteresis;
                    return value >= lower - Tolerance(lower)
                        && value <= upper + Tolerance(upper);
                }

                case MvkSetpointComparison.Inside:
                {
                    double lower = setpoint.Threshold - setpoint.Hysteresis;
                    double upper = setpoint.UpperThreshold!.Value + setpoint.Hysteresis;
                    return value <= lower + Tolerance(lower)
                        || value >= upper - Tolerance(upper);
                }

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(setpoint),
                        setpoint.Comparison,
                        "Неизвестное правило сравнения.");
            }
        }

        private static double Tolerance(double value)
        {
            return Math.Max(1e-9, Math.Abs(value) * 1e-6);
        }

        private MvkSetpointStatus CreateStatus(MvkSetpoint setpoint)
        {
            SetpointState state = states[setpoint.Id.Trim()];

            return new MvkSetpointStatus
            {
                Setpoint = setpoint,
                State = state.State,
                LastValue = state.LastValue,
                Timestamp = state.Timestamp,
                ActiveSince = state.ActiveSince,
                Error = state.Error
            };
        }

        private static MvkAlarmEvent CreateEvent(
            MvkSetpoint setpoint,
            DateTimeOffset now,
            MvkSetpointState state,
            MvkSetpointState previous,
            float value,
            MvkModbusException? error)
        {
            return new MvkAlarmEvent
            {
                Timestamp = now,
                DeviceId = setpoint.DeviceId.Trim(),
                Setpoint = setpoint,
                State = state,
                PreviousState = previous,
                Value = value,
                Error = error,
                Message = setpoint.Message
            };
        }

        private sealed class SetpointState
        {
            public MvkSetpointState State { get; set; } = MvkSetpointState.Normal;

            public float LastValue { get; set; } = float.NaN;

            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.MinValue;

            public DateTimeOffset? PendingSince { get; set; }

            public DateTimeOffset? ActiveSince { get; set; }

            public MvkModbusException? Error { get; set; }
        }

        private readonly struct MeasurementKey : IEquatable<MeasurementKey>
        {
            public MeasurementKey(string deviceId, int measurement)
            {
                DeviceId = deviceId;
                Measurement = measurement;
            }

            public string DeviceId { get; }

            public int Measurement { get; }

            public bool Equals(MeasurementKey other)
            {
                return Measurement == other.Measurement
                    && string.Equals(DeviceId, other.DeviceId, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object? obj)
            {
                return obj is MeasurementKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceId),
                    Measurement);
            }
        }
    }
}
