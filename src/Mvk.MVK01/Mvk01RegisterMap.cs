namespace Mvk.MVK01
{
    /// <summary>
    /// Карта регистров МВК01 (ПО 1.0.61+), адреса — в 16-битных регистрах Modbus.
    /// Источник: MVK01_registers.txt.
    /// </summary>
    public static class Mvk01RegisterMap
    {
        /// <summary>
        /// Максимальное число каналов АЦП.
        /// </summary>
        public const int MaxChannels = 8;

        /// <summary>
        /// Максимальное число каналов тахометра.
        /// </summary>
        public const int MaxTachoChannels = 2;

        /// <summary>
        /// Число полос дольоктавного спектра.
        /// </summary>
        public const int SpectrumBands = 43;

        /// <summary>
        /// Максимальный номер расчётного параметра (раздел 5.1.2).
        /// </summary>
        public const int MaxMeasurementNumber = 184;

        // ------------------------------------------------------------------
        // Раздел 3.1.1 — общие параметры устройства (чтение/запись)
        // ------------------------------------------------------------------

        /// <summary>Системная команда/статус выполнения (uint32, раздел 3.1.2).</summary>
        public const ushort SystemCommand = 0x0002;

        /// <summary>IP-адрес устройства (4 байта).</summary>
        public const ushort IpAddress = 0x0004;

        /// <summary>Маска подсети (4 байта).</summary>
        public const ushort SubnetMask = 0x0006;

        /// <summary>Адрес основного шлюза (4 байта).</summary>
        public const ushort Gateway = 0x0008;

        /// <summary>Номер TCP-порта Modbus/TCP.</summary>
        public const ushort ModbusPort = 0x000A;

        /// <summary>Порядок передачи байт для Modbus-клиента (раздел 3.1.4).</summary>
        public const ushort ByteOrder = 0x000C;

        /// <summary>Частота дискретизации DSP (только чтение).</summary>
        public const ushort SampleRate = 0x000E;

        /// <summary>Максимальное значение частоты вращения (тахометр), Гц.</summary>
        public const ushort MaxRpmFrequency = 0x0010;

        /// <summary>Минимальное значение частоты вращения (тахометр), Гц.</summary>
        public const ushort MinRpmFrequency = 0x0012;

        /// <summary>Режим контроля и диагностики (раздел 3.1.5).</summary>
        public const ushort ControlMode = 0x0014;

        /// <summary>Число каналов, относящихся к 1-му агрегату (1–7; 0 = по умолчанию 4).</summary>
        public const ushort ChannelsPerUnit = 0x0016;

        /// <summary>Таймаут отсутствия активности Modbus-клиента, с.</summary>
        public const ushort ClientTimeout = 0x0018;

        /// <summary>Тип данных дольоктавного спектра (0-ускорение, 1-скорость, 2-перемещение).</summary>
        public const ushort SpectrumDataType = 0x001A;

        /// <summary>Калибровочные коэффициенты каналов АЦП (8 × 4 байта).</summary>
        public const ushort AdcCalibration = 0x0020;

        /// <summary>Тип дополнительного модуля UART (P7).</summary>
        public const ushort UartModuleType = 0x0040;

        // ------------------------------------------------------------------
        // Раздел 3.2.1 — параметры измерительных каналов АЦП (чтение/запись)
        // ------------------------------------------------------------------

        /// <summary>Базовый адрес блока конфигурации первого канала.</summary>
        public const ushort ChannelConfigBase = 0x0100;

        /// <summary>Размер блока конфигурации канала в регистрах.</summary>
        public const ushort ChannelConfigBlockSize = 0x0100;

        /// <summary>Тип канала (смещение).</summary>
        public const ushort ChannelTypeOffset = 0x0000;

        /// <summary>Коэффициент преобразования датчика, В/(м/с2) (смещение).</summary>
        public const ushort ChannelSensorFactorOffset = 0x0002;

        /// <summary>Порог перегрузки в долях (смещение).</summary>
        public const ushort ChannelOverloadThresholdOffset = 0x0004;

        /// <summary>Резерв: коэффициент пересчёта (смещение, не писать).</summary>
        public const ushort ChannelConversionFactorOffset = 0x000E;

        /// <summary>Наименование единиц измеряемой величины (32 байта).</summary>
        public const ushort ChannelUnitsOffset = 0x0010;

        /// <summary>Серийный номер датчика (32 байта).</summary>
        public const ushort ChannelSerialOffset = 0x0020;

        /// <summary>Фильтр X…25 кГц — номер нижней частоты среза.</summary>
        public const ushort ChannelFilterX25kHzOffset = 0x003E;

        /// <summary>Фильтр 1 — номер нижней дольоктавы.</summary>
        public const ushort ChannelFilter1LowOffset = 0x0040;

        /// <summary>Фильтр 1 — номер верхней дольоктавы.</summary>
        public const ushort ChannelFilter1HighOffset = 0x0042;

        /// <summary>Фильтр 2 — номер нижней дольоктавы.</summary>
        public const ushort ChannelFilter2LowOffset = 0x0044;

        /// <summary>Фильтр 2 — номер верхней дольоктавы.</summary>
        public const ushort ChannelFilter2HighOffset = 0x0046;

        /// <summary>Максимально допустимое постоянное напряжение на датчике, В.</summary>
        public const ushort ChannelMaxDcVoltageOffset = 0x0050;

        /// <summary>Минимально допустимое постоянное напряжение на датчике, В.</summary>
        public const ushort ChannelMinDcVoltageOffset = 0x0052;

        /// <summary>Конфигурация основного контролируемого параметра (32 байта).</summary>
        public const ushort ChannelMainControlledParamOffset = 0x0060;

        /// <summary>Конфигурация контролируемых параметров 2–8 (7 × 32 байта).</summary>
        public const ushort ChannelControlledParamsOffset = 0x0070;

        /// <summary>Размер блока конфигурации контролируемого параметра в регистрах.</summary>
        public const ushort ControlledParamSize = 16;

        /// <summary>
        /// Адрес начала блока конфигурации канала АЦП.
        /// </summary>
        /// <param name="channel">Номер канала (1–8).</param>
        public static ushort ChannelConfig(int channel)
        {
            return (ushort)(ChannelConfigBase + (channel - 1) * ChannelConfigBlockSize);
        }

        // ------------------------------------------------------------------
        // Раздел 3.3.1 — параметры каналов тахометра (чтение/запись)
        // ------------------------------------------------------------------

        /// <summary>Базовый адрес блока конфигурации первого тахометра.</summary>
        public const ushort TachoConfigBase = 0x0900;

        /// <summary>Размер блока конфигурации тахометра в регистрах.</summary>
        public const ushort TachoConfigBlockSize = 0x0100;

        /// <summary>Гистерезис (мин. время срабатывания), в 1/65536 долях секунды.</summary>
        public const ushort TachoConfigHysteresisOffset = 0x0006;

        /// <summary>Дополнительная функция (0-нет, 1-кнопка квитирования).</summary>
        public const ushort TachoConfigAdditionalFunctionOffset = 0x0008;

        /// <summary>Серийный номер датчика тахометра (32 байта).</summary>
        public const ushort TachoConfigSerialOffset = 0x0020;

        /// <summary>
        /// Адрес начала блока конфигурации тахометра.
        /// </summary>
        /// <param name="tacho">Номер тахометра (1–2).</param>
        public static ushort TachoConfig(int tacho)
        {
            return (ushort)(TachoConfigBase + (tacho - 1) * TachoConfigBlockSize);
        }

        // ------------------------------------------------------------------
        // Раздел 4.1.1 — системные параметры, общие (только чтение)
        // ------------------------------------------------------------------

        /// <summary>Серийный номер устройства (8 байт).</summary>
        public const ushort DeviceSerialNumber = 0x1000;

        /// <summary>Версия внутреннего ПО (uint32, раздел 4.1.2).</summary>
        public const ushort FirmwareVersion = 0x1004;

        /// <summary>Индикатор системных ошибок (uint32, раздел 4.1.3).</summary>
        public const ushort SystemErrorIndicators = 0x1006;

        /// <summary>Счётчик времени работы, с.</summary>
        public const ushort Uptime = 0x1008;

        /// <summary>Размер установленной карты памяти, КБ.</summary>
        public const ushort MemoryCardSize = 0x100A;

        /// <summary>MD5-хэш внутреннего ПО DSP (16 байт).</summary>
        public const ushort DspMd5 = 0x100C;

        /// <summary>Состояние контроля по агрегату 1 (раздел 4.1.4).</summary>
        public const ushort Unit1ControlState = 0x1014;

        /// <summary>Состояние контроля по агрегату 2 (раздел 4.1.4).</summary>
        public const ushort Unit2ControlState = 0x1016;

        /// <summary>Индикаторы работы DSP (3 × 4 байта, раздел 4.1.5).</summary>
        public const ushort DspIndicators = 0x1020;

        // ------------------------------------------------------------------
        // Раздел 5.1.1 — расчётные параметры каналов АЦП (по каналам, только чтение)
        // ------------------------------------------------------------------

        /// <summary>Базовый адрес расчётных параметров первого канала.</summary>
        public const ushort ChannelDataBase = 0x2000;

        /// <summary>Размер блока расчётных параметров канала в регистрах.</summary>
        public const ushort ChannelDataBlockSize = 0x0200;

        /// <summary>Напряжение на датчике, В (смещение).</summary>
        public const ushort SensorVoltageOffset = 0x0000;

        /// <summary>Дольоктавный спектр, 43 полосы × 2 регистра (смещение).</summary>
        public const ushort SpectrumOffset = 0x0002;

        /// <summary>Число регистров дольоктавного спектра.</summary>
        public const ushort SpectrumRegisterCount = SpectrumBands * 2;

        /// <summary>Пик-фактор виброускорения X…25 кГц (смещение).</summary>
        public const ushort PeakFactorOffset = 0x005C;

        /// <summary>СКЗ виброускорения X…25 кГц, м/с2 (смещение).</summary>
        public const ushort RmsX25kHzOffset = 0x005E;

        /// <summary>Наборы СКЗ (8 наборов × 3 значения), смещение.</summary>
        public const ushort RmsSetOffset = 0x0060;

        /// <summary>Число наборов СКЗ.</summary>
        public const int RmsSetCount = 8;

        /// <summary>Число значений в наборе СКЗ (ускорение, скорость, перемещение).</summary>
        public const int RmsSetValues = 3;

        /// <summary>Число регистров всех наборов СКЗ.</summary>
        public const ushort RmsSetRegisterCount = 48;

        /// <summary>Постоянная составляющая в канале, м/с2 (смещение).</summary>
        public const ushort DcComponentOffset = 0x009A;

        /// <summary>Копия напряжения на датчике (параметр 79), смещение.</summary>
        public const ushort SensorVoltageCopyOffset = 0x009C;

        /// <summary>Индикаторы состояния канала (uint32, раздел 5.1.4), смещение.</summary>
        public const ushort ChannelStateOffset = 0x009E;

        /// <summary>Эквивалентные пиковые значения, 24 значения (смещение).</summary>
        public const ushort EquivalentPeakOffset = 0x00A0;

        /// <summary>Число эквивалентных значений (пиковых и размаха).</summary>
        public const int EquivalentValuesCount = 24;

        /// <summary>Число регистров эквивалентных значений.</summary>
        public const ushort EquivalentRegisterCount = 48;

        /// <summary>Эквивалентные значения размаха, 24 значения (смещение).</summary>
        public const ushort EquivalentRangeOffset = 0x00E0;

        /// <summary>Копия индикаторов состояния канала (смещение).</summary>
        public const ushort ChannelStateCopyOffset = 0x0120;

        /// <summary>Индикаторы контроля параметров 1–8 (8 × 8 регистров), смещение.</summary>
        public const ushort ControlIndicatorsOffset = 0x0130;

        /// <summary>Число регистров индикаторов контроля (8 параметров × 8 регистров).</summary>
        public const ushort ControlIndicatorsRegisterCount = 64;

        /// <summary>Размер блока индикаторов одного контролируемого параметра, регистры.</summary>
        public const ushort ControlIndicatorSize = 8;

        /// <summary>Фаза 1–4 гармоник частоты вращения (абсолютное смещение от базы канала).</summary>
        public const ushort HarmonicsPhaseOffset = 0x7034;

        /// <summary>Амплитуда 1–4 гармоник частоты вращения (абсолютное смещение от базы канала).</summary>
        public const ushort HarmonicsAmplitudeOffset = 0x703C;

        /// <summary>
        /// Адрес начала блока расчётных параметров канала АЦП.
        /// </summary>
        /// <param name="channel">Номер канала (1–8).</param>
        public static ushort ChannelData(int channel)
        {
            return (ushort)(ChannelDataBase + (channel - 1) * ChannelDataBlockSize);
        }

        // ------------------------------------------------------------------
        // Раздел 5.1.2 — расчётные параметры по номерам параметров (только чтение)
        // ------------------------------------------------------------------

        /// <summary>Базовый адрес блока параметров, адресация по номерам.</summary>
        public const ushort MeasurementByNumberBase = 0x4000;

        /// <summary>Шаг между параметрами (8 каналов × 2 регистра).</summary>
        public const ushort MeasurementByNumberStride = 16;

        /// <summary>Число регистров одного параметра (8 каналов × 2 регистра).</summary>
        public const ushort MeasurementByNumberRegisterCount = 16;

        /// <summary>Число каналов в блоке параметра.</summary>
        public const int MeasurementByNumberChannels = 8;

        /// <summary>
        /// Адрес параметра по его номеру (раздел 5.1.2).
        /// </summary>
        /// <param name="number">Номер параметра (1–184).</param>
        public static ushort MeasurementByNumber(int number)
        {
            return (ushort)(MeasurementByNumberBase + (number - 1) * MeasurementByNumberStride);
        }

        // ------------------------------------------------------------------
        // Раздел 5.2.1 — расчётные параметры каналов тахометра (только чтение)
        // ------------------------------------------------------------------

        /// <summary>Базовый адрес расчётных параметров первого тахометра.</summary>
        public const ushort TachoDataBase = 0x3000;

        /// <summary>Размер блока расчётных параметров тахометра в регистрах.</summary>
        public const ushort TachoDataBlockSize = 0x0100;

        /// <summary>Частота вращения, Гц (смещение).</summary>
        public const ushort TachoFrequencyOffset = 0x0000;

        /// <summary>Среднее приращение частоты вращения на оборот, Гц (смещение).</summary>
        public const ushort TachoAverageIncrementOffset = 0x0002;

        /// <summary>Ошибка датчика оборотов (смещение).</summary>
        public const ushort TachoSensorErrorOffset = 0x0004;

        /// <summary>Временная метка последнего перехода, 1/65536 с (смещение).</summary>
        public const ushort TachoTimestampOffset = 0x0006;

        /// <summary>Текущее значение уровня на входе тахометра (смещение).</summary>
        public const ushort TachoInputLevelOffset = 0x0008;

        /// <summary>Направление вращения (только для 1-го тахометра), смещение.</summary>
        public const ushort TachoDirectionOffset = 0x000A;

        /// <summary>Число зарегистрированных переходов 0→1 (смещение).</summary>
        public const ushort TachoTransitionsOffset = 0x000C;

        /// <summary>Число регистров блока расчётных данных тахометра.</summary>
        public const ushort TachoDataRegisterCount = 0x000E;

        /// <summary>
        /// Адрес начала блока расчётных параметров тахометра.
        /// </summary>
        /// <param name="tacho">Номер тахометра (1–2).</param>
        public static ushort TachoData(int tacho)
        {
            return (ushort)(TachoDataBase + (tacho - 1) * TachoDataBlockSize);
        }
    }
}
