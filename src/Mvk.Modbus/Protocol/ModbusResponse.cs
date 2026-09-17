using System;

namespace Mvk.Modbus.Protocol
{
    /// <summary>
    /// Результат выполнения <see cref="ModbusRequest"/>: данные ответа либо ошибка.
    /// </summary>
    public sealed class ModbusResponse
    {
        private static readonly ushort[] EmptyData = [];

        /// <summary>
        /// Признак успешного выполнения запроса.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// Данные ответа (для операций чтения). Для операций записи — пустой массив.
        /// </summary>
        public ushort[] Data { get; }

        /// <summary>
        /// Ошибка выполнения запроса. Равен <see langword="null"/> при успехе.
        /// </summary>
        public MvkModbusException? Error { get; }

        private ModbusResponse(bool isSuccess, ushort[] data, MvkModbusException? error)
        {
            IsSuccess = isSuccess;
            Data = data;
            Error = error;
        }

        /// <summary>
        /// Создаёт успешный ответ с данными.
        /// </summary>
        /// <param name="data">Данные ответа; для операций записи допускается пустой массив.</param>
        public static ModbusResponse Success(ushort[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new ModbusResponse(true, data, null);
        }

        /// <summary>
        /// Создаёт успешный ответ для операции записи.
        /// </summary>
        public static ModbusResponse WriteSuccess()
        {
            return new ModbusResponse(true, EmptyData, null);
        }

        /// <summary>
        /// Создаёт ответ с ошибкой.
        /// </summary>
        /// <param name="error">Ошибка выполнения запроса.</param>
        public static ModbusResponse Failure(MvkModbusException error)
        {
            ArgumentNullException.ThrowIfNull(error);
            return new ModbusResponse(false, EmptyData, error);
        }

        /// <summary>
        /// Создаёт ответ с ошибкой из произвольного исключения.
        /// </summary>
        /// <param name="exception">Исключение транспорта или обработки.</param>
        /// <param name="deviceId">Идентификатор устройства из конфигурации.</param>
        public static ModbusResponse Failure(Exception exception, string? deviceId = null)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is MvkModbusException mvkException
                ? Failure(mvkException)
                : Failure(new MvkModbusException(exception.Message, MvkErrorKind.Transport, deviceId, null, exception));
        }
    }
}
