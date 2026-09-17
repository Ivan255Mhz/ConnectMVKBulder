namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Состояние устройства в пуле.
    /// </summary>
    public enum MvkDeviceState
    {
        /// <summary>
        /// Соединение не установлено.
        /// </summary>
        Disconnected = 0,

        /// <summary>
        /// Выполняется подключение.
        /// </summary>
        Connecting = 1,

        /// <summary>
        /// Соединение установлено, устройство отвечает.
        /// </summary>
        Connected = 2,

        /// <summary>
        /// Соединение потеряно или устройство не отвечает; ожидается реконнект.
        /// </summary>
        Faulted = 3
    }
}
