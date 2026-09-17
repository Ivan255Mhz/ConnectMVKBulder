using System;

namespace Mvk.Modbus
{
    /// <summary>
    /// Исключение при работе с Modbus-устройством. Содержит категорию ошибки
    /// <see cref="MvkErrorKind"/>, идентификатор устройства и код ошибки Modbus.
    /// </summary>
    public class MvkModbusException : Exception
    {
        /// <summary>
        /// Категория ошибки.
        /// </summary>
        public MvkErrorKind ErrorKind { get; }

        /// <summary>
        /// Идентификатор устройства из конфигурации (может быть не задан).
        /// </summary>
        public string? DeviceId { get; }

        /// <summary>
        /// Код ошибки Modbus из ответа (exception response). Заполняется только для
        /// <see cref="MvkErrorKind.DeviceError"/>.
        /// </summary>
        public byte? ModbusExceptionCode { get; }

        /// <summary>
        /// Признак временной ошибки, которую имеет смысл повторять (транспорт или таймаут).
        /// </summary>
        public bool IsTransient => ErrorKind is MvkErrorKind.Transport or MvkErrorKind.Timeout or MvkErrorKind.Busy;

        /// <summary>
        /// Создаёт исключение с неизвестной категорией ошибки.
        /// </summary>
        /// <param name="message">Текст ошибки.</param>
        public MvkModbusException(string message)
            : this(message, MvkErrorKind.Unknown)
        {
        }

        /// <summary>
        /// Создаёт исключение с указанной категорией ошибки.
        /// </summary>
        /// <param name="message">Текст ошибки.</param>
        /// <param name="errorKind">Категория ошибки.</param>
        public MvkModbusException(string message, MvkErrorKind errorKind)
            : this(message, errorKind, null, null, null)
        {
        }

        /// <summary>
        /// Создаёт исключение с указанной категорией ошибки и вложенным исключением.
        /// </summary>
        /// <param name="message">Текст ошибки.</param>
        /// <param name="errorKind">Категория ошибки.</param>
        /// <param name="innerException">Вложенное исключение.</param>
        public MvkModbusException(string message, MvkErrorKind errorKind, Exception? innerException)
            : this(message, errorKind, null, null, innerException)
        {
        }

        /// <summary>
        /// Создаёт исключение с полным набором данных об ошибке.
        /// </summary>
        /// <param name="message">Текст ошибки.</param>
        /// <param name="errorKind">Категория ошибки.</param>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        /// <param name="modbusExceptionCode">Код ошибки Modbus из ответа устройства.</param>
        /// <param name="innerException">Вложенное исключение.</param>
        public MvkModbusException(
            string message,
            MvkErrorKind errorKind,
            string? deviceId,
            byte? modbusExceptionCode = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorKind = errorKind;
            DeviceId = deviceId;
            ModbusExceptionCode = modbusExceptionCode;
        }
    }
}
