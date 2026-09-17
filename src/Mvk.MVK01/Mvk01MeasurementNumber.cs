namespace Mvk.MVK01
{
    /// <summary>
    /// Номера расчётных параметров МВК01 для адресации по номерам (раздел 5.1.2).
    /// Перечислены основные параметры; остальные доступны по числовому номеру (1–184).
    /// </summary>
    public enum Mvk01MeasurementNumber
    {
        /// <summary>Напряжение на датчике, В.</summary>
        SensorVoltage = 1,

        /// <summary>Полоса дольоктавного спектра 1 (0.63 Гц).</summary>
        SpectrumBand1 = 2,

        /// <summary>Полоса дольоктавного спектра 43 (10 кГц).</summary>
        SpectrumBand43 = 44,

        /// <summary>Пик-фактор виброускорения X…25 кГц.</summary>
        PeakFactorAcceleration = 47,

        /// <summary>СКЗ виброускорения X…25 кГц, м/с2.</summary>
        RmsX25kHzAcceleration = 48,

        /// <summary>СКЗ виброускорения 10…3000 Гц, м/с2.</summary>
        Rms10To3000Acceleration = 49,

        /// <summary>СКЗ виброскорости 10…3000 Гц, мм/с.</summary>
        Rms10To3000Velocity = 50,

        /// <summary>СКЗ виброперемещения 10…3000 Гц, мкм.</summary>
        Rms10To3000Displacement = 51,

        /// <summary>СКЗ виброускорения 10…2000 Гц, м/с2.</summary>
        Rms10To2000Acceleration = 52,

        /// <summary>СКЗ виброскорости 10…2000 Гц, мм/с.</summary>
        Rms10To2000Velocity = 53,

        /// <summary>СКЗ виброперемещения 10…2000 Гц, мкм.</summary>
        Rms10To2000Displacement = 54,

        /// <summary>СКЗ виброускорения 10…1000 Гц, м/с2.</summary>
        Rms10To1000Acceleration = 55,

        /// <summary>СКЗ виброскорости 10…1000 Гц, мм/с.</summary>
        Rms10To1000Velocity = 56,

        /// <summary>СКЗ виброперемещения 10…1000 Гц, мкм.</summary>
        Rms10To1000Displacement = 57,

        /// <summary>СКЗ виброускорения 2…1000 Гц, м/с2.</summary>
        Rms2To1000Acceleration = 58,

        /// <summary>СКЗ виброскорости 2…1000 Гц, мм/с.</summary>
        Rms2To1000Velocity = 59,

        /// <summary>СКЗ виброперемещения 2…1000 Гц, мкм.</summary>
        Rms2To1000Displacement = 60,

        /// <summary>СКЗ виброускорения 0.8…300 Гц, м/с2.</summary>
        Rms0_8To300Acceleration = 61,

        /// <summary>СКЗ виброскорости 0.8…300 Гц, мм/с.</summary>
        Rms0_8To300Velocity = 62,

        /// <summary>СКЗ виброперемещения 0.8…300 Гц, мкм.</summary>
        Rms0_8To300Displacement = 63,

        /// <summary>СКЗ виброускорения 0.8…150 Гц, м/с2.</summary>
        Rms0_8To150Acceleration = 64,

        /// <summary>СКЗ виброскорости 0.8…150 Гц, мм/с.</summary>
        Rms0_8To150Velocity = 65,

        /// <summary>СКЗ виброперемещения 0.8…150 Гц, мкм.</summary>
        Rms0_8To150Displacement = 66,

        /// <summary>СКЗ виброускорения по фильтру 1, м/с2.</summary>
        RmsFilter1Acceleration = 67,

        /// <summary>СКЗ виброскорости по фильтру 1, мм/с.</summary>
        RmsFilter1Velocity = 68,

        /// <summary>СКЗ виброперемещения по фильтру 1, мкм.</summary>
        RmsFilter1Displacement = 69,

        /// <summary>СКЗ виброускорения по фильтру 2, м/с2.</summary>
        RmsFilter2Acceleration = 70,

        /// <summary>СКЗ виброскорости по фильтру 2, мм/с.</summary>
        RmsFilter2Velocity = 71,

        /// <summary>СКЗ виброперемещения по фильтру 2, мкм.</summary>
        RmsFilter2Displacement = 72,

        /// <summary>Постоянная составляющая в канале, м/с2.</summary>
        DcComponent = 78,

        /// <summary>Напряжение на датчике (копия параметра 1), В.</summary>
        SensorVoltageCopy = 79,

        /// <summary>Индикаторы состояния канала (раздел 5.1.4).</summary>
        ChannelState = 80,

        /// <summary>Начало блока эквивалентных пиковых значений (24 параметра: 81–104).</summary>
        EquivalentPeakStart = 81,

        /// <summary>Начало блока эквивалентных значений размаха (24 параметра: 123–136).</summary>
        EquivalentRangeStart = 123,

        /// <summary>Индикаторы состояния канала (копия, параметр 145).</summary>
        ChannelStateCopy = 145,

        /// <summary>Значение контролируемого параметра 1 (параметры 153–184).</summary>
        ControlParameter1Value = 153
    }
}
