using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Устройство пула: последовательно выполняет запросы через пайплайн,
    /// отслеживает состояние, планирует реконнект и отключает простаивающее соединение.
    /// </summary>
    public sealed class MvkDevice : IMvkDevice
    {
        private readonly IMvkTransport transport;
        private readonly MvkPipeline pipeline;
        private readonly SemaphoreSlim requestGate;
        private readonly ILogger logger;
        private readonly object stateLock = new();

        private MvkDeviceState state = MvkDeviceState.Disconnected;
        private MvkModbusException? lastError;
        private DateTimeOffset? nextReconnectAt;
        private int consecutiveFailures;
        private long lastRequestTicks;
        private long lastActivityTicks;
        private bool disposed;

        /// <summary>
        /// Создаёт устройство пула.
        /// </summary>
        /// <param name="settings">Параметры устройства.</param>
        /// <param name="transport">Транспорт устройства.</param>
        /// <param name="pipeline">Пайплайн обработки запросов.</param>
        /// <param name="logger">Логгер (необязателен).</param>
        public MvkDevice(
            MvkDeviceSettings settings,
            IMvkTransport transport,
            MvkPipeline pipeline,
            ILogger<MvkDevice>? logger = null)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.logger = logger ?? NullLogger<MvkDevice>.Instance;

            requestGate = new SemaphoreSlim(
                Math.Max(1, settings.MaxConcurrentRequests),
                Math.Max(1, settings.MaxConcurrentRequests));
        }

        /// <inheritdoc/>
        public MvkDeviceSettings Settings { get; }

        /// <inheritdoc/>
        public MvkDeviceState State
        {
            get
            {
                lock (stateLock)
                {
                    return state;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsConnected => transport.IsConnected;

        /// <inheritdoc/>
        public MvkModbusException? LastError
        {
            get
            {
                lock (stateLock)
                {
                    return lastError;
                }
            }
        }

        /// <inheritdoc/>
        public DateTimeOffset LastRequestAt
        {
            get
            {
                long ticks = Interlocked.Read(ref lastRequestTicks);
                return ticks == 0 ? DateTimeOffset.MinValue : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        /// <inheritdoc/>
        public event EventHandler<MvkDeviceStateChangedEventArgs>? StateChanged;

        /// <inheritdoc/>
        public async Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!Settings.Enabled)
            {
                return ModbusResponse.Failure(new MvkModbusException(
                    $"Устройство '{Settings.Id}' отключено в конфигурации (Enabled = false).",
                    MvkErrorKind.DeviceDisabled,
                    Settings.Id));
            }

            await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                Interlocked.Exchange(ref lastRequestTicks, now.UtcDateTime.Ticks);

                ModbusRequestContext context = new(Settings, request, cancellationToken);
                ModbusResponse response = await pipeline.ExecuteAsync(context).ConfigureAwait(false);

                ApplyResult(response);
                return response;
            }
            finally
            {
                Interlocked.Exchange(ref lastActivityTicks, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
                requestGate.Release();
            }
        }

        /// <inheritdoc/>
        public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (!Settings.Enabled)
            {
                throw new MvkModbusException(
                    $"Устройство '{Settings.Id}' отключено в конфигурации (Enabled = false).",
                    MvkErrorKind.DeviceDisabled,
                    Settings.Id);
            }

            if (transport.IsConnected)
            {
                SetState(MvkDeviceState.Connected);
                return;
            }

            SetState(MvkDeviceState.Connecting);

            try
            {
                await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);
                MarkHealthy();
                SetState(MvkDeviceState.Connected);
            }
            catch (OperationCanceledException)
            {
                SetState(MvkDeviceState.Disconnected);
                throw;
            }
            catch (MvkModbusException ex)
            {
                MarkFaulted(ex);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            await transport.DisconnectAsync().ConfigureAwait(false);
            SetState(MvkDeviceState.Disconnected);
        }

        /// <inheritdoc/>
        public Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        {
            return ExecuteAndUnwrapAsync(
                ModbusRequest.ReadHoldingRegisters(Settings.Id, address, quantity),
                cancellationToken);
        }

        /// <inheritdoc/>
        public Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default)
        {
            return ExecuteAndUnwrapAsync(
                ModbusRequest.ReadInputRegisters(Settings.Id, address, quantity),
                cancellationToken);
        }

        /// <inheritdoc/>
        public async Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default)
        {
            await ExecuteAndUnwrapAsync(
                ModbusRequest.WriteSingleRegister(Settings.Id, address, value),
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken cancellationToken = default)
        {
            await ExecuteAndUnwrapAsync(
                ModbusRequest.WriteMultipleRegisters(Settings.Id, address, data),
                cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            transport.Dispose();
            requestGate.Dispose();
        }

        /// <summary>
        /// Проверяет необходимость реконнекта или отключения простаивающего соединения.
        /// </summary>
        /// <param name="now">Текущее время (UTC).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        internal async Task MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (disposed)
            {
                return;
            }

            if (ShouldReconnect(now))
            {
                try
                {
                    await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);

                    logger.LogInformation(
                        "МВК '{DeviceId}': соединение восстановлено [{Endpoint}].",
                        Settings.Id,
                        Settings.Endpoint);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (MvkModbusException ex)
                {
                    logger.LogDebug(
                        "МВК '{DeviceId}': реконнект не удался — {ErrorKind}: {Message}",
                        Settings.Id,
                        ex.ErrorKind,
                        ex.Message);
                }
            }
            else if (IsIdle(now))
            {
                await DisconnectAsync(cancellationToken).ConfigureAwait(false);

                logger.LogDebug(
                    "МВК '{DeviceId}': соединение закрыто после простоя ({IdleSeconds} с).",
                    Settings.Id,
                    Settings.IdleDisconnectSeconds);
            }
        }

        private bool ShouldReconnect(DateTimeOffset now)
        {
            lock (stateLock)
            {
                return Settings.Enabled
                    && state == MvkDeviceState.Faulted
                    && nextReconnectAt is not null
                    && now >= nextReconnectAt;
            }
        }

        private bool IsIdle(DateTimeOffset now)
        {
            if (Settings.IdleDisconnectSeconds <= 0 || !transport.IsConnected)
            {
                return false;
            }

            long ticks = Interlocked.Read(ref lastActivityTicks);
            DateTimeOffset lastActivity = ticks == 0
                ? now
                : new DateTimeOffset(ticks, TimeSpan.Zero);

            return now - lastActivity >= TimeSpan.FromSeconds(Settings.IdleDisconnectSeconds);
        }

        private void ApplyResult(ModbusResponse response)
        {
            MvkModbusException? error = response.Error;

            if (response.IsSuccess)
            {
                MarkHealthy();
                SetState(transport.IsConnected ? MvkDeviceState.Connected : MvkDeviceState.Disconnected);
                return;
            }

            if (error?.IsTransient == true)
            {
                MarkFaulted(error);
                return;
            }

            SetState(
                transport.IsConnected ? MvkDeviceState.Connected : MvkDeviceState.Disconnected,
                error);
        }

        private void MarkHealthy()
        {
            lock (stateLock)
            {
                consecutiveFailures = 0;
                nextReconnectAt = null;
                lastError = null;
            }
        }

        private void MarkFaulted(MvkModbusException error)
        {
            lock (stateLock)
            {
                consecutiveFailures++;
                lastError = error;

                double delayMs = Math.Min(
                    Settings.ReconnectBaseDelayMs * Math.Pow(2, Math.Max(0, consecutiveFailures - 1)),
                    Settings.ReconnectMaxDelayMs);

                nextReconnectAt = DateTimeOffset.UtcNow.AddMilliseconds(delayMs);
            }

            SetState(MvkDeviceState.Faulted, error);
        }

        private void SetState(MvkDeviceState newState, MvkModbusException? error = null)
        {
            MvkDeviceState oldState;

            lock (stateLock)
            {
                if (state == newState)
                {
                    if (error is not null)
                    {
                        lastError = error;
                    }

                    return;
                }

                oldState = state;
                state = newState;

                if (error is not null)
                {
                    lastError = error;
                }
            }

            StateChanged?.Invoke(this, new MvkDeviceStateChangedEventArgs(this, oldState, newState, error));
        }

        private async Task<ushort[]> ExecuteAndUnwrapAsync(ModbusRequest request, CancellationToken cancellationToken)
        {
            ModbusResponse response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                return response.Data;
            }

            throw response.Error!;
        }
    }
}
