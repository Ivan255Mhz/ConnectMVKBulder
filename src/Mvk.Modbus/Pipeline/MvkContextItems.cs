namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Ключи для <see cref="ModbusRequestContext.Items"/>.
    /// </summary>
    public static class MvkContextItems
    {
        /// <summary>
        /// Общий таймаут операции в миллисекундах (int). Устанавливает вызывающий код,
        /// например типизированный слой для команды сохранения конфигурации (3000 мс).
        /// </summary>
        public const string TimeoutMs = "mvk.timeoutMs";

        /// <summary>
        /// Признак короткого замыкания пайплайна без обращения к устройству (bool).
        /// </summary>
        public const string ShortCircuited = "mvk.shortCircuited";
    }
}
