using System;
using System.Collections.Generic;
using Mvk.Modbus;

namespace Mvk.MVK01
{
    /// <summary>
    /// Снимок общих сведений об устройстве (раздел 4.1.1).
    /// </summary>
    public sealed record Mvk01DeviceInfo
    {
        /// <summary>Время формирования снимка (UTC).</summary>
        public DateTimeOffset CapturedAt { get; init; }

        /// <summary>Серийный номер устройства.</summary>
        public required string SerialNumber { get; init; }

        /// <summary>Версия внутреннего ПО.</summary>
        public required Mvk01FirmwareVersion FirmwareVersion { get; init; }

        /// <summary>Индикатор системных ошибок.</summary>
        public required Mvk01SystemErrors Errors { get; init; }

        /// <summary>Счётчик времени работы.</summary>
        public required TimeSpan Uptime { get; init; }

        /// <summary>Размер установленной карты памяти, КБ.</summary>
        public required int MemoryCardKilobytes { get; init; }

        /// <summary>MD5-хэш внутреннего ПО DSP (шестнадцатеричная строка).</summary>
        public required string DspMd5 { get; init; }

        /// <summary>Состояние контроля по агрегату 1.</summary>
        public required Mvk01UnitState Unit1State { get; init; }

        /// <summary>Состояние контроля по агрегату 2.</summary>
        public required Mvk01UnitState Unit2State { get; init; }

        /// <summary>Индикаторы работы DSP.</summary>
        public required Mvk01DspIndicators DspIndicators { get; init; }
    }

    /// <summary>
    /// Снимок расчётных параметров канала АЦП (раздел 5.1.1).
    /// </summary>
    public sealed record Mvk01Channel
    {
        /// <summary>Время формирования снимка (UTC).</summary>
        public DateTimeOffset CapturedAt { get; init; }

        /// <summary>
        /// Согласованность снимка. <see langword="null"/> — проверка не выполнялась
        /// (<c>ReadChannelAsync</c>), <see langword="true"/> — копии регистров совпали,
        /// <see langword="false"/> — обнаружено расхождение после всех попыток.
        /// </summary>
        public bool? IsConsistent { get; init; }

        /// <summary>Число попыток чтения снимка.</summary>
        public int ReadAttempts { get; init; } = 1;

        /// <summary>Номер канала (1–8).</summary>
        public required int Channel { get; init; }

        /// <summary>Напряжение на датчике, В.</summary>
        public required float SensorVoltage { get; init; }

        /// <summary>Дольоктавный спектр, 43 полосы (единицы по типу спектра устройства).</summary>
        public required float[] Spectrum { get; init; }

        /// <summary>Пик-фактор виброускорения X…25 кГц.</summary>
        public required float PeakFactor { get; init; }

        /// <summary>СКЗ виброускорения X…25 кГц, м/с2.</summary>
        public required float RmsX25kHzAcceleration { get; init; }

        /// <summary>Наборы СКЗ по полосам частот.</summary>
        public required IReadOnlyList<Mvk01RmsSet> RmsSets { get; init; }

        /// <summary>Постоянная составляющая в канале, м/с2.</summary>
        public required float DcComponent { get; init; }

        /// <summary>Индикаторы состояния канала.</summary>
        public required Mvk01ChannelState State { get; init; }

        /// <summary>Эквивалентные пиковые значения (24 значения, соответствуют параметрам 49–72).</summary>
        public required float[] EquivalentPeakValues { get; init; }

        /// <summary>Эквивалентные значения размаха (24 значения, соответствуют параметрам 49–72).</summary>
        public required float[] EquivalentRangeValues { get; init; }

        /// <summary>
        /// Возвращает значение полосы спектра.
        /// </summary>
        /// <param name="bandNumber">Номер полосы (1–43).</param>
        public float GetSpectrumBand(int bandNumber)
        {
            Mvk01SpectrumBands.GetCenterFrequency(bandNumber);
            return Spectrum[bandNumber - 1];
        }
    }

    /// <summary>
    /// Снимок расчётных параметров канала тахометра (раздел 5.2.1).
    /// </summary>
    public sealed record Mvk01TachoMeasurement
    {
        /// <summary>Время формирования снимка (UTC).</summary>
        public DateTimeOffset CapturedAt { get; init; }

        /// <summary>Номер тахометра (1–2).</summary>
        public required int Tacho { get; init; }

        /// <summary>Частота вращения, Гц.</summary>
        public required float FrequencyHz { get; init; }

        /// <summary>Среднее приращение частоты вращения на один оборот, Гц.</summary>
        public required float AverageIncrementHz { get; init; }

        /// <summary>Ошибка датчика оборотов (период меньше минимально допустимого).</summary>
        public required bool SensorError { get; init; }

        /// <summary>Временная метка последнего перехода (с).</summary>
        public required double TimestampSeconds { get; init; }

        /// <summary>Текущее значение уровня на входе тахометра.</summary>
        public required bool InputLevel { get; init; }

        /// <summary>Направление вращения (только для 1-го тахометра при энкодере).</summary>
        public required Mvk01RotationDirection Direction { get; init; }

        /// <summary>Число зарегистрированных переходов 0→1.</summary>
        public required uint TransitionCount { get; init; }
    }

    /// <summary>
    /// Снимок конфигурации измерительного канала (раздел 3.2.1).
    /// </summary>
    public sealed record Mvk01ChannelConfig
    {
        /// <summary>Время формирования снимка (UTC).</summary>
        public DateTimeOffset CapturedAt { get; init; }

        /// <summary>Номер канала (1–8).</summary>
        public required int Channel { get; init; }

        /// <summary>Тип канала (по документации = 0).</summary>
        public required int ChannelType { get; init; }

        /// <summary>Коэффициент преобразования датчика, В/(м/с2).</summary>
        public required float SensorFactor { get; init; }

        /// <summary>Порог перегрузки в долях.</summary>
        public required float OverloadThreshold { get; init; }

        /// <summary>Наименование единиц измеряемой величины.</summary>
        public required string Units { get; init; }

        /// <summary>Серийный номер датчика.</summary>
        public required string SerialNumber { get; init; }

        /// <summary>Фильтр X…25 кГц — нижняя частота среза.</summary>
        public required Mvk01FilterX25kHz FilterX25kHz { get; init; }

        /// <summary>Фильтр 1 — номер нижней дольоктавы.</summary>
        public required int Filter1LowBand { get; init; }

        /// <summary>Фильтр 1 — номер верхней дольоктавы.</summary>
        public required int Filter1HighBand { get; init; }

        /// <summary>Фильтр 2 — номер нижней дольоктавы.</summary>
        public required int Filter2LowBand { get; init; }

        /// <summary>Фильтр 2 — номер верхней дольоктавы.</summary>
        public required int Filter2HighBand { get; init; }

        /// <summary>Максимально допустимое постоянное напряжение на датчике, В.</summary>
        public required float MaxDcVoltage { get; init; }

        /// <summary>Минимально допустимое постоянное напряжение на датчике, В.</summary>
        public required float MinDcVoltage { get; init; }
    }

    /// <summary>
    /// Результат выполнения системной команды.
    /// </summary>
    public sealed record Mvk01CommandResult
    {
        /// <summary>Отправленная команда.</summary>
        public required Mvk01SystemCommand Command { get; init; }

        /// <summary>Итоговый статус.</summary>
        public required Mvk01CommandStatus Status { get; init; }

        /// <summary>Статус, прочитанный из устройства (если получен).</summary>
        public uint? DeviceStatus { get; init; }

        /// <summary>Ошибка транспорта (если была).</summary>
        public MvkModbusException? Error { get; init; }

        /// <summary>Команда выполнена успешно.</summary>
        public bool IsSuccess => Status == Mvk01CommandStatus.Success;

        /// <inheritdoc/>
        public override string ToString()
        {
            return Error is not null
                ? $"Команда {Command}: {Status} ({Error.Message})"
                : $"Команда {Command}: {Status}";
        }
    }
}
