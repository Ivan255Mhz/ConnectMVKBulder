using System;
using System.Linq;
using Mvk.Modbus;
using Mvk.MVK01.Polling;
using Mvk.MVK01.Setpoints;
using Xunit;

namespace Mvk.MVK01.Tests.Setpoints
{
    public sealed class MvkSetpointEngineTests
    {
        private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        [Fact]
        public void Above_ActivatesAndClearsWithHysteresis()
        {
            MvkSetpointEngine engine = CreateEngine(Setpoint(4.5, hysteresis: 0.2));

            MvkAlarmEvent activated = Assert.Single(engine.Evaluate(Sample(4.6f), T0));
            Assert.Equal(MvkSetpointState.Active, activated.State);
            Assert.Equal(MvkSetpointState.Normal, activated.PreviousState);
            Assert.True(activated.IsActive);
            Assert.Equal(4.6f, activated.Value);

            Assert.Empty(engine.Evaluate(Sample(4.4f), T0.AddSeconds(1)));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            MvkAlarmEvent cleared = Assert.Single(engine.Evaluate(Sample(4.2f), T0.AddSeconds(2)));
            Assert.Equal(MvkSetpointState.Normal, cleared.State);
            Assert.Equal(MvkSetpointState.Active, cleared.PreviousState);
            Assert.True(cleared.IsClear);
        }

        [Fact]
        public void Below_ActivatesAndClearsWithHysteresis()
        {
            MvkSetpointEngine engine = CreateEngine(
                Setpoint(1.0, comparison: MvkSetpointComparison.Below, hysteresis: 0.1));

            Assert.Single(engine.Evaluate(Sample(0.9f), T0));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(1.05f), T0.AddSeconds(1)));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Single(engine.Evaluate(Sample(1.1f), T0.AddSeconds(2)));
            Assert.Equal(MvkSetpointState.Normal, engine.Statuses[0].State);
        }

        [Fact]
        public void Outside_ActivatesAndClearsWithHysteresis()
        {
            MvkSetpointEngine engine = CreateEngine(
                Setpoint(
                    0.5,
                    comparison: MvkSetpointComparison.Outside,
                    upper: 8.0,
                    hysteresis: 0.2));

            Assert.Single(engine.Evaluate(Sample(0.4f), T0));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(0.6f), T0.AddSeconds(1)));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Single(engine.Evaluate(Sample(0.7f), T0.AddSeconds(2)));
            Assert.Equal(MvkSetpointState.Normal, engine.Statuses[0].State);
        }

        [Fact]
        public void Inside_ActivatesAndClearsWithHysteresis()
        {
            MvkSetpointEngine engine = CreateEngine(
                Setpoint(
                    0.5,
                    comparison: MvkSetpointComparison.Inside,
                    upper: 8.0,
                    hysteresis: 0.2));

            Assert.Single(engine.Evaluate(Sample(4.0f), T0));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(8.1f), T0.AddSeconds(1)));
            Assert.Equal(MvkSetpointState.Active, engine.Statuses[0].State);

            Assert.Single(engine.Evaluate(Sample(8.2f), T0.AddSeconds(2)));
            Assert.Equal(MvkSetpointState.Normal, engine.Statuses[0].State);
        }

        [Fact]
        public void ActivationDelay_RequiresConditionToHold()
        {
            MvkSetpointEngine engine = CreateEngine(
                Setpoint(4.5, delay: TimeSpan.FromSeconds(1)));

            Assert.Empty(engine.Evaluate(Sample(5f), T0));
            Assert.Equal(MvkSetpointState.Pending, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(5f), T0.AddMilliseconds(500)));
            Assert.Equal(MvkSetpointState.Pending, engine.Statuses[0].State);

            MvkAlarmEvent activated = Assert.Single(
                engine.Evaluate(Sample(5f), T0.AddSeconds(1)));

            Assert.Equal(MvkSetpointState.Active, activated.State);
            Assert.Equal(MvkSetpointState.Pending, activated.PreviousState);
        }

        [Fact]
        public void ActivationDelay_ResetWhenConditionDisappears()
        {
            MvkSetpointEngine engine = CreateEngine(
                Setpoint(4.5, delay: TimeSpan.FromSeconds(1)));

            Assert.Empty(engine.Evaluate(Sample(5f), T0));
            Assert.Equal(MvkSetpointState.Pending, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(4f), T0.AddMilliseconds(500)));
            Assert.Equal(MvkSetpointState.Normal, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(5f), T0.AddSeconds(2)));
            Assert.Equal(MvkSetpointState.Pending, engine.Statuses[0].State);
        }

        [Fact]
        public void MultipleSetpoints_ProduceSeparateEvents()
        {
            MvkSetpoint warning = Setpoint(
                4.5,
                severity: MvkAlarmSeverity.Warning,
                id: "warning");

            MvkSetpoint alarm = Setpoint(
                7.1,
                severity: MvkAlarmSeverity.Alarm,
                id: "alarm");

            MvkSetpointEngine engine = CreateEngine(warning, alarm);

            MvkAlarmEvent[] events = engine.Evaluate(Sample(7.5f), T0).ToArray();

            Assert.Equal(2, events.Length);
            Assert.Contains(events, item => item.Setpoint.Id == "warning");
            Assert.Contains(events, item => item.Setpoint.Id == "alarm");
            Assert.All(events, item => Assert.Equal(MvkSetpointState.Active, item.State));
            Assert.Equal(2, engine.Statuses.Count);
        }

        [Fact]
        public void Error_MarksUnknownOnceAndRecovers()
        {
            MvkSetpointEngine engine = CreateEngine(Setpoint(4.5));
            MvkModbusException error = new("нет связи", MvkErrorKind.Transport, "mvk-1");

            MvkAlarmEvent first = Assert.Single(
                engine.Evaluate(Sample(0f, error: error), T0));

            Assert.Equal(MvkSetpointState.Unknown, first.State);
            Assert.Equal(MvkSetpointState.Normal, first.PreviousState);
            Assert.Same(error, first.Error);
            Assert.Equal(MvkSetpointState.Unknown, engine.Statuses[0].State);

            Assert.Empty(engine.Evaluate(Sample(0f, error: error), T0.AddSeconds(1)));

            MvkAlarmEvent recovered = Assert.Single(
                engine.Evaluate(Sample(1f), T0.AddSeconds(2)));

            Assert.Equal(MvkSetpointState.Normal, recovered.State);
            Assert.Equal(MvkSetpointState.Unknown, recovered.PreviousState);
        }

        [Fact]
        public void NaN_MarksUnknown()
        {
            MvkSetpointEngine engine = CreateEngine(Setpoint(4.5));

            MvkAlarmEvent unknown = Assert.Single(
                engine.Evaluate(Sample(float.NaN), T0));

            Assert.Equal(MvkSetpointState.Unknown, unknown.State);
            Assert.True(float.IsNaN(unknown.Value));
            Assert.Equal(MvkSetpointState.Unknown, engine.Statuses[0].State);
        }

        [Fact]
        public void DisabledSetpoint_IsIgnored()
        {
            MvkSetpointEngine engine = CreateEngine(Setpoint(4.5, enabled: false));

            Assert.Empty(engine.Evaluate(Sample(10f), T0));
            Assert.Equal(MvkSetpointState.Normal, engine.Statuses[0].State);
            Assert.Empty(engine.CreatePollItems("mvk-1"));
        }

        [Fact]
        public void CreatePollItems_ReturnsOneItemPerMeasurement()
        {
            MvkSetpoint differentMeasurement = Setpoint(3, id: "c", channel: 2) with
            {
                Measurement = Mvk01MeasurementNumber.RmsX25kHzAcceleration
            };

            MvkSetpointEngine engine = CreateEngine(
                Setpoint(1, id: "a", channel: 1),
                Setpoint(2, id: "b", channel: 2),
                differentMeasurement);

            var items = engine.CreatePollItems("mvk-1");

            Assert.Equal(2, items.Count);
            Assert.Contains(
                items,
                item => (int)item.Number == (int)Mvk01MeasurementNumber.Rms10To1000Acceleration);
            Assert.Contains(
                items,
                item => (int)item.Number == (int)Mvk01MeasurementNumber.RmsX25kHzAcceleration);
        }

        [Fact]
        public void Evaluate_WithoutDeviceId_Throws()
        {
            MvkSetpointEngine engine = CreateEngine(Setpoint(4.5));
            Mvk01Sample sample = new()
            {
                Timestamp = T0,
                Item = new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, 1),
                Values = [5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]
            };

            Assert.Throws<InvalidOperationException>(() => engine.Evaluate(sample, T0));
        }

        [Fact]
        public void Constructor_InvalidSetpoint_ThrowsValidationException()
        {
            MvkSetpoint invalid = Setpoint(4.5) with { Channel = 9 };

            MvkSetpointValidationException exception = Assert.Throws<MvkSetpointValidationException>(
                () => new MvkSetpointEngine([invalid]));

            Assert.NotEmpty(exception.Failures);
        }

        [Fact]
        public void Constructor_OutsideHysteresisTooLarge_ThrowsValidationException()
        {
            MvkSetpoint invalid = Setpoint(
                0.5,
                comparison: MvkSetpointComparison.Outside,
                upper: 1.0,
                hysteresis: 0.3);

            Assert.Throws<MvkSetpointValidationException>(
                () => new MvkSetpointEngine([invalid]));
        }

        private static MvkSetpointEngine CreateEngine(params MvkSetpoint[] setpoints)
        {
            return new MvkSetpointEngine(setpoints);
        }

        private static MvkSetpoint Setpoint(
            double threshold,
            MvkSetpointComparison comparison = MvkSetpointComparison.Above,
            double? upper = null,
            double hysteresis = 0,
            TimeSpan? delay = null,
            MvkAlarmSeverity severity = MvkAlarmSeverity.Warning,
            bool enabled = true,
            string id = "setpoint",
            int channel = 1)
        {
            return new MvkSetpoint
            {
                Id = id,
                DeviceId = "mvk-1",
                Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                Channel = channel,
                Comparison = comparison,
                Threshold = threshold,
                UpperThreshold = upper,
                Hysteresis = hysteresis,
                ActivationDelay = delay ?? TimeSpan.Zero,
                Severity = severity,
                Enabled = enabled,
                Name = id
            };
        }

        private static Mvk01Sample Sample(
            float value,
            MvkModbusException? error = null,
            string deviceId = "mvk-1")
        {
            float[] values = error is null
                ? [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]
                : [];

            if (error is null)
            {
                values[0] = value;
            }

            return new Mvk01Sample
            {
                Timestamp = T0,
                DeviceId = deviceId,
                Item = new Mvk01PollItem(Mvk01MeasurementNumber.Rms10To1000Acceleration, 1),
                Values = values,
                Error = error
            };
        }
    }
}
