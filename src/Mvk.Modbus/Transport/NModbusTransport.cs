using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using NModbus;
using NModbus.Device;

namespace Mvk.Modbus.Transport
{
    /// <summary>
    /// Транспорт Modbus/TCP на базе NModbus с таймаутами из параметров устройства,
    /// ленивым подключением и автоматическим разрывом при ошибке.
    /// </summary>
    public sealed class NModbusTransport : IMvkTransport
    {
        private static readonly IModbusFactory ModbusFactory = new ModbusFactory();

        private readonly MvkDeviceSettings settings;
        private readonly ILogger logger;
        private readonly SemaphoreSlim connectLock = new(1, 1);

        private TcpClient? tcpClient;
        private IModbusMaster? master;
        private bool connected;
        private bool disposed;

        /// <summary>
        /// Создаёт транспорт для устройства.
        /// </summary>
        /// <param name="settings">Параметры устройства.</param>
        /// <param name="logger">Логгер (необязателен).</param>
        public NModbusTransport(MvkDeviceSettings settings, ILogger<NModbusTransport>? logger = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.logger = logger ?? NullLogger<NModbusTransport>.Instance;
        }

        /// <inheritdoc/>
        public bool IsConnected => connected && tcpClient is { Connected: true };

        /// <inheritdoc/>
        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            await connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsConnected)
                {
                    return;
                }

                CloseConnection();

                TcpClient tcp = new();
                using CancellationTokenSource timeoutSource =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(settings.ConnectTimeoutMs);

                try
                {
                    await tcp.ConnectAsync(settings.Host, settings.Port, timeoutSource.Token).ConfigureAwait(false);

                    IModbusMaster newMaster = ModbusFactory.CreateMaster(tcp);
                    newMaster.Transport.ReadTimeout = settings.ReadTimeoutMs;
                    newMaster.Transport.WriteTimeout = settings.WriteTimeoutMs;

                    tcpClient = tcp;
                    master = newMaster;
                    connected = true;

                    logger.LogDebug(
                        "МВК '{DeviceId}': соединение установлено [{Endpoint}], SlaveId {SlaveId}.",
                        settings.Id,
                        settings.Endpoint,
                        settings.SlaveId);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    tcp.Dispose();
                    throw new MvkModbusException(
                        $"Таймаут подключения к {settings.Endpoint} ({settings.ConnectTimeoutMs} мс).",
                        MvkErrorKind.Timeout,
                        settings.Id);
                }
                catch (OperationCanceledException)
                {
                    tcp.Dispose();
                    throw;
                }
                catch (SocketException ex)
                {
                    tcp.Dispose();
                    throw new MvkModbusException(
                        $"Не удалось подключиться к {settings.Endpoint}: {ex.Message}",
                        MvkErrorKind.Transport,
                        settings.Id,
                        null,
                        ex);
                }
                catch (Exception ex) when (ex is not MvkModbusException)
                {
                    tcp.Dispose();
                    throw new MvkModbusException(
                        $"Не удалось подключиться к {settings.Endpoint}: {ex.Message}",
                        MvkErrorKind.Transport,
                        settings.Id,
                        null,
                        ex);
                }
            }
            finally
            {
                connectLock.Release();
            }
        }

        /// <inheritdoc/>
        public Task DisconnectAsync()
        {
            CloseConnection();
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!IsConnected)
                {
                    await ConnectAsync(cancellationToken).ConfigureAwait(false);
                }

                IModbusMaster currentMaster = master!;

                if (request.IsWrite)
                {
                    switch (request.Operation)
                    {
                        case ModbusOperation.WriteSingleRegister:
                            await currentMaster
                                .WriteSingleRegisterAsync(settings.SlaveId, request.StartAddress, request.WriteData![0])
                                .ConfigureAwait(false);
                            break;

                        case ModbusOperation.WriteMultipleRegisters:
                            await currentMaster
                                .WriteMultipleRegistersAsync(settings.SlaveId, request.StartAddress, request.WriteData!)
                                .ConfigureAwait(false);
                            break;

                        default:
                            return ModbusResponse.Failure(new MvkModbusException(
                                $"Операция {request.Operation} не поддерживается транспортом.",
                                MvkErrorKind.Validation,
                                settings.Id));
                    }

                    return ModbusResponse.WriteSuccess();
                }

                ushort[] data = request.Operation switch
                {
                    ModbusOperation.ReadHoldingRegisters => await currentMaster
                        .ReadHoldingRegistersAsync(settings.SlaveId, request.StartAddress, request.Quantity)
                        .ConfigureAwait(false),

                    ModbusOperation.ReadInputRegisters => await currentMaster
                        .ReadInputRegistersAsync(settings.SlaveId, request.StartAddress, request.Quantity)
                        .ConfigureAwait(false),

                    _ => throw new MvkModbusException(
                        $"Операция {request.Operation} не поддерживается транспортом.",
                        MvkErrorKind.Validation,
                        settings.Id)
                };

                return ModbusResponse.Success(data);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MvkModbusException ex)
            {
                CloseConnection();
                logger.LogWarning(ex, "МВК '{DeviceId}': ошибка обращения [{Endpoint}].", settings.Id, settings.Endpoint);
                return ModbusResponse.Failure(ex);
            }
            catch (Exception ex)
            {
                CloseConnection();
                MvkModbusException error = MapException(settings, ex);
                logger.LogWarning(ex, "МВК '{DeviceId}': ошибка обращения [{Endpoint}].", settings.Id, settings.Endpoint);
                return ModbusResponse.Failure(error);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CloseConnection();
            connectLock.Dispose();
        }

        /// <summary>
        /// Преобразует исключение NModbus/.NET в типизированную ошибку библиотеки.
        /// </summary>
        internal static MvkModbusException MapException(MvkDeviceSettings settings, Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                switch (current)
                {
                    case TimeoutException:
                        return new MvkModbusException(
                            $"Таймаут при обращении к {settings.Endpoint}.",
                            MvkErrorKind.Timeout,
                            settings.Id,
                            null,
                            exception);

                    case SlaveException slave:
                        return new MvkModbusException(
                            $"Устройство '{settings.Id}' вернуло ошибку Modbus 0x{slave.SlaveExceptionCode:X2} "
                            + $"(функция 0x{slave.FunctionCode:X2}).",
                            MvkErrorKind.DeviceError,
                            settings.Id,
                            slave.SlaveExceptionCode,
                            exception);

                    case SocketException socket when socket.SocketErrorCode == SocketError.TimedOut:
                        return new MvkModbusException(
                            $"Таймаут при обращении к {settings.Endpoint}.",
                            MvkErrorKind.Timeout,
                            settings.Id,
                            null,
                            exception);
                }
            }

            if (exception is IOException io && IsTimeoutMessage(io.Message))
            {
                return new MvkModbusException(
                    $"Таймаут при обращении к {settings.Endpoint}.",
                    MvkErrorKind.Timeout,
                    settings.Id,
                    null,
                    exception);
            }

            return new MvkModbusException(
                $"Ошибка при обращении к {settings.Endpoint}: {exception.Message}",
                MvkErrorKind.Transport,
                settings.Id,
                null,
                exception);
        }

        private static bool IsTimeoutMessage(string message)
        {
            return message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || message.Contains("timed out", StringComparison.OrdinalIgnoreCase);
        }

        private void CloseConnection()
        {
            connected = false;

            try
            {
                master?.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                tcpClient?.Close();
                tcpClient?.Dispose();
            }
            catch (Exception)
            {
            }

            master = null;
            tcpClient = null;
        }
    }
}
