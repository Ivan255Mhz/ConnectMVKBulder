namespace Mvk.MVK01
{
    /// <summary>
    /// Системные команды МВК01 (регистр 0x0002, раздел 3.1.2).
    /// </summary>
    public enum Mvk01SystemCommand : uint
    {
        /// <summary>Восстановить конфигурацию из Flash.</summary>
        RestoreConfiguration = 0x10001,

        /// <summary>
        /// Сохранить текущую конфигурацию во Flash. Таймаут ответа увеличить до 3 с.
        /// </summary>
        SaveConfiguration = 0xF0001,

        /// <summary>Перезапустить устройство.</summary>
        Reboot = 0xA0001
    }

    /// <summary>
    /// Результат выполнения системной команды.
    /// </summary>
    public enum Mvk01CommandStatus
    {
        /// <summary>Операция успешно выполнена (0x00000).</summary>
        Success = 0x00000,

        /// <summary>Клиент записал неверную команду (0x00BAD).</summary>
        BadCommand = 0x00BAD,

        /// <summary>Ошибка флэш-памяти, конфигурация не сохранена (0xF0BAD).</summary>
        FlashError = 0xF0BAD,

        /// <summary>Команда принята, статус не определён (продолжается ожидание).</summary>
        Pending = -1,

        /// <summary>Истёк таймаут ожидания статуса.</summary>
        Timeout = -2,

        /// <summary>Ошибка транспорта при отправке команды или чтении статуса.</summary>
        TransportError = -3
    }

    /// <summary>
    /// Зона контроля агрегата (раздел 4.1.4, биты 0–7).
    /// </summary>
    public enum Mvk01Zone : byte
    {
        /// <summary>Зона не определена.</summary>
        Undefined = 0x00,

        /// <summary>Зелёная зона (0x11).</summary>
        Green = 0x11,

        /// <summary>Отказ датчика (0x12).</summary>
        SensorFailure = 0x12,

        /// <summary>Неверная конфигурация (0x13).</summary>
        InvalidConfiguration = 0x13,

        /// <summary>Жёлтая зона (0x14).</summary>
        Yellow = 0x14,

        /// <summary>Красная зона (0x15).</summary>
        Red = 0x15
    }

    /// <summary>
    /// Направление вращения (раздел 5.2.2, только для 1-го тахометра при энкодере).
    /// </summary>
    public enum Mvk01RotationDirection : uint
    {
        /// <summary>Частота меньше минимально допустимой.</summary>
        BelowMinimum = 0x00,

        /// <summary>Вращение от тахометра 1 к тахометру 2.</summary>
        Tacho1ToTacho2 = 0x02,

        /// <summary>Вращение от тахометра 2 к тахометру 1.</summary>
        Tacho2ToTacho1 = 0x03
    }

    /// <summary>
    /// Тип данных дольоктавного спектра (регистр 0x001A).
    /// </summary>
    public enum Mvk01SpectrumDataType : uint
    {
        /// <summary>Ускорение.</summary>
        Acceleration = 0,

        /// <summary>Скорость.</summary>
        Velocity = 1,

        /// <summary>Перемещение.</summary>
        Displacement = 2
    }

    /// <summary>
    /// Фильтр X…25 кГц — номер нижней частоты среза (смещение канала 0x3E).
    /// </summary>
    public enum Mvk01FilterX25kHz : uint
    {
        /// <summary>Выключен.</summary>
        Off = 0,

        /// <summary>6.4 кГц.</summary>
        Hz6400 = 1,

        /// <summary>10 кГц.</summary>
        Hz10000 = 2,

        /// <summary>15 кГц.</summary>
        Hz15000 = 3
    }

    /// <summary>
    /// Тип дополнительного модуля UART (регистр 0x0040).
    /// </summary>
    public enum Mvk01UartModuleType : uint
    {
        /// <summary>Нет.</summary>
        None = 0,

        /// <summary>КПА-1В.</summary>
        Kpa1V = 1,

        /// <summary>ВДМ82.</summary>
        Vdm82 = 3
    }
}
