namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Политика записи в регистры устройства.
    /// </summary>
    public enum MvkWritePolicy
    {
        /// <summary>
        /// Запись разрешена во все регистры.
        /// </summary>
        AllowAll = 0,

        /// <summary>
        /// Запись запрещена в защищённую область конфигурации; остальные регистры доступны.
        /// </summary>
        ProtectConfigArea = 1,

        /// <summary>
        /// Запись запрещена полностью (устройство только для чтения).
        /// </summary>
        ReadOnly = 2
    }
}
