namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Порядок передачи байт для 32-битных значений (регистр 0x000C устройства МВК01).
    /// </summary>
    public enum MvkByteOrder
    {
        /// <summary>
        /// Big Endian, старшее слово первым (значение регистра 0x00000000, по умолчанию).
        /// </summary>
        BigEndian = 0,

        /// <summary>
        /// Little Endian, полный обратный порядок байт (значение регистра 0x66666666).
        /// </summary>
        LittleEndian = 1,

        /// <summary>
        /// Смешанный режим, слова меняются местами (значение регистра 0x99999999, для ВДМ82).
        /// </summary>
        Mixed = 2
    }
}
