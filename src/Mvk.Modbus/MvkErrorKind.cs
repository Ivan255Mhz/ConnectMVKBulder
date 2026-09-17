namespace Mvk.Modbus
{
    /// <summary>
    /// Категория ошибки при работе с Modbus-устройством.
    /// </summary>
    public enum MvkErrorKind
    {
        /// <summary>
        /// Неизвестная ошибка.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Ошибка транспорта: сеть, сокет, разрыв соединения.
        /// </summary>
        Transport = 1,

        /// <summary>
        /// Истёк таймаут подключения или ожидания ответа устройства.
        /// </summary>
        Timeout = 2,

        /// <summary>
        /// Устройство вернуло код ошибки Modbus (exception response).
        /// </summary>
        DeviceError = 3,

        /// <summary>
        /// Устройство не найдено в пуле.
        /// </summary>
        DeviceNotFound = 4,

        /// <summary>
        /// Устройство отключено в конфигурации (Enabled = false).
        /// </summary>
        DeviceDisabled = 5,

        /// <summary>
        /// Некорректный запрос или нарушение политики записи.
        /// </summary>
        Validation = 6,

        /// <summary>
        /// Операция отменена вызывающим кодом.
        /// </summary>
        Canceled = 7,

        /// <summary>
        /// Устройство занято: очередь запросов переполнена или превышено время ожидания.
        /// </summary>
        Busy = 8,

        /// <summary>
        /// Ошибка конфигурации устройства или секции настроек.
        /// </summary>
        Configuration = 9
    }
}
