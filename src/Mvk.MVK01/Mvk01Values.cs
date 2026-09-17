using System;

namespace Mvk.MVK01
{
    /// <summary>
    /// Версия внутреннего ПО МВК01 (раздел 4.1.2): версия.подверсия1.подверсия2.сборка.
    /// </summary>
    public readonly record struct Mvk01FirmwareVersion(int Major, int Minor, int Patch, int Build)
    {
        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Major}.{Minor}.{Patch}.{Build}";
        }
    }

    /// <summary>
    /// Индикатор системных ошибок МВК01 (раздел 4.1.3).
    /// </summary>
    public readonly record struct Mvk01SystemErrors(
        bool DspReceiveError,
        bool SdCardError,
        bool DspSendError,
        bool ModbusInitializationError,
        bool ConfigurationCrcError,
        uint Raw)
    {
        /// <summary>
        /// Ошибок нет.
        /// </summary>
        public bool IsClean => Raw == 0;
    }

    /// <summary>
    /// Состояние контроля по агрегату (раздел 4.1.4).
    /// </summary>
    public readonly record struct Mvk01UnitState(
        Mvk01Zone Zone,
        bool SensorFailureEvent,
        bool EnteredYellowZone,
        bool EnteredRedZone,
        uint Raw);

    /// <summary>
    /// Индикаторы работы DSP (раздел 4.1.5).
    /// </summary>
    public readonly record struct Mvk01DspIndicators(
        byte OverloadChannelMask,
        uint ConfigurationFlags,
        uint CalculationCounter)
    {
        /// <summary>
        /// Перегрузка измерительного входа канала (1–8).
        /// </summary>
        /// <param name="channel">Номер канала (1–8).</param>
        public bool IsChannelOverloaded(int channel)
        {
            if (channel < 1 || channel > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(channel), channel, "Номер канала должен быть в диапазоне 1..8.");
            }

            return (OverloadChannelMask & (1 << (channel - 1))) != 0;
        }
    }

    /// <summary>
    /// Индикаторы состояния канала АЦП (раздел 5.1.4).
    /// </summary>
    public readonly record struct Mvk01ChannelState(
        bool Overload,
        bool Buffer16HzNotFilled,
        bool Buffer64HzNotFilled,
        bool Buffer512HzNotFilled,
        bool DcVoltageAboveMaximum,
        bool DcVoltageBelowMinimum,
        uint Raw);

    /// <summary>
    /// Набор СКЗ канала: ускорение, скорость, перемещение в одной полосе частот.
    /// </summary>
    /// <param name="Name">Название полосы (например, «10…3000 Гц»).</param>
    /// <param name="Acceleration">СКЗ виброускорения, м/с2.</param>
    /// <param name="Velocity">СКЗ виброскорости, мм/с.</param>
    /// <param name="Displacement">СКЗ виброперемещения, мкм.</param>
    public sealed record Mvk01RmsSet(
        string Name,
        float Acceleration,
        float Velocity,
        float Displacement);
}
