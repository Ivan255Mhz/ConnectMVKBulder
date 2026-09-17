using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Запрещает запись по политике устройства: полный запрет (<see cref="MvkWritePolicy.ReadOnly"/>)
    /// или защита области конфигурации (<see cref="MvkWritePolicy.ProtectConfigArea"/>).
    /// Чтение не ограничивается.
    /// </summary>
    public sealed class WriteProtectionMiddleware : IModbusMiddleware
    {
        /// <inheritdoc/>
        public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            MvkModbusException? denial = Check(context);
            if (denial is not null)
            {
                context.Response = ModbusResponse.Failure(denial);
                return Task.CompletedTask;
            }

            return next(context, cancellationToken);
        }

        /// <summary>
        /// Проверяет политику записи и возвращает ошибку либо <see langword="null"/>.
        /// </summary>
        /// <param name="context">Контекст запроса.</param>
        public static MvkModbusException? Check(ModbusRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            ModbusRequest request = context.Request;
            MvkDeviceSettings settings = context.Settings;

            if (!request.IsWrite)
            {
                return null;
            }

            if (settings.WritePolicy == MvkWritePolicy.ReadOnly)
            {
                return new MvkModbusException(
                    $"Устройство '{settings.Id}' доступно только для чтения (WritePolicy=ReadOnly).",
                    MvkErrorKind.Validation,
                    settings.Id);
            }

            if (settings.WritePolicy == MvkWritePolicy.ProtectConfigArea
                && !settings.AllowConfigurationWrite
                && OverlapsProtectedArea(request, settings))
            {
                return new MvkModbusException(
                    $"Запись в защищённую область конфигурации (0x{settings.ProtectedWriteStartAddress:X4}–0x{settings.ProtectedWriteEndAddress:X4}) "
                    + $"устройства '{settings.Id}' запрещена. Разрешите AllowConfigurationWrite.",
                    MvkErrorKind.Validation,
                    settings.Id);
            }

            return null;
        }

        private static bool OverlapsProtectedArea(ModbusRequest request, MvkDeviceSettings settings)
        {
            return request.StartAddress <= settings.ProtectedWriteEndAddress
                && request.EndAddress >= settings.ProtectedWriteStartAddress;
        }
    }
}
