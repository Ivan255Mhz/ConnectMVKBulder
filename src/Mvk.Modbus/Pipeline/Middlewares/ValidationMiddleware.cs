using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Проверяет запрос перед обращением к устройству: ограничения Modbus и особенности
    /// устройства (например, требование чётных адреса и числа регистров у МВК01).
    /// При нарушении коротко замыкает пайплайн ошибкой <see cref="MvkErrorKind.Validation"/>.
    /// </summary>
    public sealed class ValidationMiddleware : IModbusMiddleware
    {
        /// <inheritdoc/>
        public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            MvkModbusException? error = Validate(context);
            if (error is not null)
            {
                context.Response = ModbusResponse.Failure(error);
                return Task.CompletedTask;
            }

            return next(context, cancellationToken);
        }

        /// <summary>
        /// Проверяет запрос и возвращает ошибку либо <see langword="null"/>.
        /// </summary>
        /// <param name="context">Контекст запроса.</param>
        public static MvkModbusException? Validate(ModbusRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ModbusRequest request = context.Request;
            MvkDeviceSettings settings = context.Settings;

            if (request.IsWrite && request.WriteData is null)
            {
                return new MvkModbusException(
                    $"Для операции {request.Operation} отсутствуют данные записи.",
                    MvkErrorKind.Validation,
                    settings.Id);
            }

            if (settings.RequireEvenAddressAndQuantity)
            {
                if (request.StartAddress % 2 != 0 || request.Quantity % 2 != 0)
                {
                    return new MvkModbusException(
                        $"Адрес 0x{request.StartAddress:X4} и число регистров {request.Quantity} должны быть чётными "
                        + $"(особенность устройства '{settings.Id}').",
                        MvkErrorKind.Validation,
                        settings.Id);
                }
            }

            return null;
        }
    }
}
