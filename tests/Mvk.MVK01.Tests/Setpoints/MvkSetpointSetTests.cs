using System;
using Mvk.MVK01.Setpoints;
using Xunit;

namespace Mvk.MVK01.Tests.Setpoints
{
    public sealed class MvkSetpointSetTests
    {
        [Fact]
        public void Fluent_WarningAndAlarm_ProducesSetpointsWithUniqueIds()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, channel: 1)
                    .Warning(4.5, hysteresis: 0.2)
                    .Alarm(7.1, hysteresis: 0.3);

            Assert.Equal(2, set.Count);
            Assert.Equal(MvkAlarmSeverity.Warning, set.Setpoints[0].Severity);
            Assert.Equal(MvkAlarmSeverity.Alarm, set.Setpoints[1].Severity);
            Assert.Equal(0.2, set.Setpoints[0].Hysteresis);
            Assert.Equal(0.3, set.Setpoints[1].Hysteresis);
            Assert.NotEqual(set.Setpoints[0].Id, set.Setpoints[1].Id);
            Assert.Empty(set.Validate());
        }

        [Fact]
        public void Fluent_BelowAndRange_HaveExpectedComparison()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, 1)
                    .Below(1.0, MvkAlarmSeverity.Alarm, hysteresis: 0.05)
                    .Outside(0.5, 8.0, MvkAlarmSeverity.Warning, hysteresis: 0.2)
                    .Inside(2.0, 3.0, MvkAlarmSeverity.Info);

            Assert.Equal(MvkSetpointComparison.Below, set.Setpoints[0].Comparison);
            Assert.Equal(1.0, set.Setpoints[0].Threshold);
            Assert.Equal(MvkSetpointComparison.Outside, set.Setpoints[1].Comparison);
            Assert.Equal(0.5, set.Setpoints[1].Threshold);
            Assert.Equal(8.0, set.Setpoints[1].UpperThreshold);
            Assert.Equal(MvkSetpointComparison.Inside, set.Setpoints[2].Comparison);
            Assert.Equal(2.0, set.Setpoints[2].Threshold);
            Assert.Equal(3.0, set.Setpoints[2].UpperThreshold);
        }

        [Fact]
        public void Fluent_ExplicitIdAndName_ArePreserved()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .Add("mvk-1", Mvk01MeasurementNumber.RmsX25kHzAcceleration, 2)
                    .Warning(12.0, name: "Канал 2: предупреждение", id: "custom-id");

            MvkSetpoint setpoint = Assert.Single(set.Setpoints);
            Assert.Equal("custom-id", setpoint.Id);
            Assert.Equal("Канал 2: предупреждение", setpoint.Name);
            Assert.Equal(2, setpoint.Channel);
        }

        [Fact]
        public void DuplicateExplicitId_IsReportedByValidation()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .Add(CreateSetpoint("same"))
                .Add(CreateSetpoint("same"));

            string failure = Assert.Single(set.Validate());
            Assert.Contains("не уникален", failure);
        }

        [Fact]
        public void InvalidChannel_ThrowsOnAdd()
        {
            MvkSetpointSet set = MvkSetpointSet.Create();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => set.Add("mvk-1", Mvk01MeasurementNumber.Rms10To1000Acceleration, 9));
        }

        [Fact]
        public void AddRange_AddsAllSetpoints()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .AddRange([CreateSetpoint("a"), CreateSetpoint("b")]);

            Assert.Equal(2, set.Count);
        }

        [Fact]
        public void ValidateAndThrow_InvalidSet_Throws()
        {
            MvkSetpointSet set = MvkSetpointSet.Create()
                .Add(CreateSetpoint("same"))
                .Add(CreateSetpoint("same"));

            MvkSetpointValidationException exception =
                Assert.Throws<MvkSetpointValidationException>(set.ValidateAndThrow);

            Assert.NotEmpty(exception.Failures);
        }

        private static MvkSetpoint CreateSetpoint(string id)
        {
            return new MvkSetpoint
            {
                Id = id,
                DeviceId = "mvk-1",
                Measurement = Mvk01MeasurementNumber.Rms10To1000Acceleration,
                Channel = 1,
                Comparison = MvkSetpointComparison.Above,
                Threshold = 1.0
            };
        }
    }
}
