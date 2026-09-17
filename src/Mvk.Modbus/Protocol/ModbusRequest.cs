using System;

namespace Mvk.Modbus.Protocol
{
    /// <summary>
    /// Неизменяемый запрос к Modbus-устройству.
    /// </summary>
    public sealed class ModbusRequest
    {
        /// <summary>
        /// Максимальное число регистров в одном запросе чтения (ограничение Modbus).
        /// </summary>
        public const int MaxReadRegisters = 125;

        /// <summary>
        /// Максимальное число регистров в одном запросе записи (ограничение Modbus).
        /// </summary>
        public const int MaxWriteRegisters = 123;

        /// <summary>
        /// Идентификатор устройства из конфигурации.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// Выполняемая операция.
        /// </summary>
        public ModbusOperation Operation { get; }

        /// <summary>
        /// Адрес первого регистра.
        /// </summary>
        public ushort StartAddress { get; }

        /// <summary>
        /// Число регистров в запросе.
        /// </summary>
        public ushort Quantity { get; }

        /// <summary>
        /// Данные для записи. Равен <see langword="null"/> для операций чтения.
        /// </summary>
        public ushort[]? WriteData { get; }

        /// <summary>
        /// Признак операции записи.
        /// </summary>
        public bool IsWrite => Operation is ModbusOperation.WriteSingleRegister or ModbusOperation.WriteMultipleRegisters;

        /// <summary>
        /// Последний затрагиваемый регистр (включительно).
        /// </summary>
        public int EndAddress => StartAddress + Quantity - 1;

        /// <summary>
        /// Создаёт запрос чтения регистров хранения (функция 0x03).
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        /// <param name="startAddress">Адрес первого регистра.</param>
        /// <param name="quantity">Число регистров (1–125).</param>
        public static ModbusRequest ReadHoldingRegisters(string deviceId, ushort startAddress, ushort quantity)
        {
            return new ModbusRequest(deviceId, ModbusOperation.ReadHoldingRegisters, startAddress, quantity, null);
        }

        /// <summary>
        /// Создаёт запрос чтения входных регистров (функция 0x04).
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        /// <param name="startAddress">Адрес первого регистра.</param>
        /// <param name="quantity">Число регистров (1–125).</param>
        public static ModbusRequest ReadInputRegisters(string deviceId, ushort startAddress, ushort quantity)
        {
            return new ModbusRequest(deviceId, ModbusOperation.ReadInputRegisters, startAddress, quantity, null);
        }

        /// <summary>
        /// Создаёт запрос записи одного регистра (функция 0x06).
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        /// <param name="address">Адрес регистра.</param>
        /// <param name="value">Записываемое значение.</param>
        public static ModbusRequest WriteSingleRegister(string deviceId, ushort address, ushort value)
        {
            return new ModbusRequest(deviceId, ModbusOperation.WriteSingleRegister, address, 1, [value]);
        }

        /// <summary>
        /// Создаёт запрос записи нескольких регистров (функция 0x10).
        /// </summary>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        /// <param name="startAddress">Адрес первого регистра.</param>
        /// <param name="data">Записываемые регистры (1–123 значения).</param>
        public static ModbusRequest WriteMultipleRegisters(string deviceId, ushort startAddress, ushort[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length is 0 or > MaxWriteRegisters)
            {
                throw new ArgumentException(
                    $"Число записываемых регистров должно быть от 1 до {MaxWriteRegisters}, получено {data.Length}.",
                    nameof(data));
            }

            return new ModbusRequest(deviceId, ModbusOperation.WriteMultipleRegisters, startAddress, (ushort)data.Length, data);
        }

        private ModbusRequest(
            string deviceId,
            ModbusOperation operation,
            ushort startAddress,
            ushort quantity,
            ushort[]? writeData)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Идентификатор устройства обязателен.", nameof(deviceId));
            }

            bool isWrite = operation is ModbusOperation.WriteSingleRegister or ModbusOperation.WriteMultipleRegisters;

            if (isWrite && writeData is null)
            {
                throw new ArgumentException($"Для операции {operation} требуются данные записи.", nameof(writeData));
            }

            if (!isWrite && writeData is not null)
            {
                throw new ArgumentException($"Операция {operation} не принимает данные записи.", nameof(writeData));
            }

            int maxQuantity = isWrite ? MaxWriteRegisters : MaxReadRegisters;
            if (quantity is 0 || quantity > maxQuantity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    quantity,
                    $"Число регистров должно быть от 1 до {maxQuantity}.");
            }

            if (writeData is not null && writeData.Length != quantity)
            {
                throw new ArgumentException(
                    $"Число значений записи ({writeData.Length}) не совпадает с числом регистров ({quantity}).",
                    nameof(writeData));
            }

            if ((int)startAddress + quantity > ushort.MaxValue + 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startAddress),
                    startAddress,
                    "Диапазон адресов выходит за границы адресного пространства Modbus.");
            }

            DeviceId = deviceId.Trim();
            Operation = operation;
            StartAddress = startAddress;
            Quantity = quantity;
            WriteData = writeData;
        }
    }
}
