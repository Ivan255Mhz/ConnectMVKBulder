namespace Mvk.Modbus.Protocol
{
    /// <summary>
    /// Операция Modbus, выполняемая над устройством.
    /// </summary>
    public enum ModbusOperation
    {
        /// <summary>
        /// Чтение регистров хранения (функция 0x03).
        /// </summary>
        ReadHoldingRegisters = 0x03,

        /// <summary>
        /// Чтение входных регистров (функция 0x04).
        /// </summary>
        ReadInputRegisters = 0x04,

        /// <summary>
        /// Запись одного регистра хранения (функция 0x06).
        /// </summary>
        WriteSingleRegister = 0x06,

        /// <summary>
        /// Запись нескольких регистров хранения (функция 0x10).
        /// </summary>
        WriteMultipleRegisters = 0x10
    }
}
