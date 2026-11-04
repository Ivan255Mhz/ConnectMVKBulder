using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.MVK01.Polling;

namespace Mvk.MVK01
{
    /// <summary>
    /// Простейшая точка входа для одного устройства: подключение по адресу без конфигурации.
    /// По умолчанию доступно только чтение; запись в конфигурацию включается параметром
    /// <c>allowConfigurationWrite</c>.
    /// <code>
    /// await using Mvk01Connection mvk = await Mvk01Connection.ConnectAsync("10.20.0.211");
    /// Mvk01Channel channel = await mvk.ReadChannelAsync(1);
    /// </code>
    /// </summary>
    public sealed class Mvk01Connection : IAsyncDisposable
    {
        private readonly MvkDevicePool pool;
        private readonly Mvk01Device device;

        private Mvk01Connection(MvkDevicePool pool, Mvk01Device device)
        {
            this.pool = pool;
            this.device = device;
        }

        /// <summary>
        /// Типизированное устройство (доступ к расширенному API и событиям состояния).
        /// </summary>
        public IMvk01Device Device => device;

        /// <summary>
        /// Подключается к устройству МВК01 по адресу и проверяет связь.
        /// </summary>
        /// <param name="host">IP-адрес или DNS-имя устройства.</param>
        /// <param name="port">TCP-порт Modbus; по умолчанию 502.</param>
        /// <param name="slaveId">Идентификатор устройства Modbus; по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <param name="allowConfigurationWrite">
        /// Разрешить запись в защищённую область конфигурации и системные команды
        /// Save/Restore/Reboot. По умолчанию запрещено.
        /// </param>
        /// <exception cref="MvkModbusException">Устройство недоступно или отключено.</exception>
        public static async Task<Mvk01Connection> ConnectAsync(
            string host,
            int port = 502,
            byte slaveId = 1,
            CancellationToken cancellationToken = default,
            bool allowConfigurationWrite = false)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new ArgumentException("Не задан адрес устройства.", nameof(host));
            }

            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions
                {
                    Port = port,
                    SlaveId = slaveId,
                    RequireEvenAddressAndQuantity = true,
                    AllowConfigurationWrite = allowConfigurationWrite
                },
                Pool = new MvkPoolOptions { ReconnectEnabled = true },
                Devices =
                [
                    new MvkDeviceOptions { Id = host.Trim(), Name = host.Trim(), Host = host.Trim() }
                ]
            };

            MvkDevicePool pool = new(options);

            try
            {
                Mvk01Device device = new(pool.GetDevice(host.Trim()));
                await device.EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
                return new Mvk01Connection(pool, device);
            }
            catch
            {
                await pool.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Читает общие сведения об устройстве.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            return device.ReadDeviceInfoAsync(cancellationToken);
        }

        /// <summary>Читает полный снимок канала АЦП.</summary>
        /// <param name="channel">Номер канала (1–8).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default)
        {
            return device.ReadChannelAsync(channel, cancellationToken);
        }

        /// <summary>Читает конфигурацию канала АЦП.</summary>
        /// <param name="channel">Номер канала (1–8).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default)
        {
            return device.ReadChannelConfigAsync(channel, cancellationToken);
        }

        /// <summary>Читает снимок канала тахометра.</summary>
        /// <param name="tacho">Номер тахометра (1–2).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default)
        {
            return device.ReadTachoAsync(tacho, cancellationToken);
        }

        /// <summary>Читает значение одного канала параметра.</summary>
        /// <param name="number">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<float> ReadValueAsync(
            Mvk01MeasurementNumber number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValueAsync(number, channel, cancellationToken);
        }

        /// <summary>Читает значение одного канала параметра по числовому номеру.</summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<float> ReadValueAsync(
            int number,
            int channel = 1,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValueAsync(number, channel, cancellationToken);
        }

        /// <summary>Читает значения параметра по всем 8 каналам.</summary>
        /// <param name="number">Номер параметра.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<float[]> ReadValuesAsync(
            Mvk01MeasurementNumber number,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValuesAsync(number, cancellationToken);
        }

        /// <summary>Читает значения параметра по всем 8 каналам по числовому номеру.</summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<float[]> ReadValuesAsync(
            int number,
            CancellationToken cancellationToken = default)
        {
            return device.ReadValuesAsync(number, cancellationToken);
        }

        /// <summary>Наблюдает за одним параметром и каналом.</summary>
        /// <param name="number">Номер параметра.</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            Mvk01MeasurementNumber number,
            int channel = 1,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            return device.WatchAsync(number, channel, interval, cancellationToken);
        }

        /// <summary>Наблюдает за одним параметром и каналом по числовому номеру.</summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="channel">Номер канала (1–8); по умолчанию 1.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            int number,
            int channel = 1,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            return device.WatchAsync(number, channel, interval, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения с токеном отмены.</summary>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            Mvk01MeasurementNumber number,
            int channel,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(number, channel, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения по числовому номеру с токеном отмены.</summary>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            int number,
            int channel,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(number, channel, cancellationToken);
        }

        /// <summary>Наблюдает за набором параметров.</summary>
        /// <param name="items">Наблюдаемые параметры.</param>
        /// <param name="interval">Интервал опроса; по умолчанию 500 мс.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            IReadOnlyList<Mvk01PollItem> items,
            TimeSpan? interval = null,
            CancellationToken cancellationToken = default)
        {
            return device.WatchAsync(items, interval, cancellationToken);
        }

        /// <summary>Короткая форма наблюдения за набором с токеном отмены.</summary>
        public IAsyncEnumerable<Mvk01Sample> WatchAsync(
            IReadOnlyList<Mvk01PollItem> items,
            CancellationToken cancellationToken)
        {
            return device.WatchAsync(items, cancellationToken);
        }

        /// <summary>Выполняет системную команду с ожиданием статуса.</summary>
        /// <param name="command">Команда.</param>
        /// <param name="timeout">Таймаут операции; по умолчанию 3 с.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01CommandResult> ExecuteSystemCommandAsync(
            Mvk01SystemCommand command,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            return device.ExecuteSystemCommandAsync(command, timeout, cancellationToken);
        }

        /// <summary>Сохраняет конфигурацию во Flash.</summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        public Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default)
        {
            return device.SaveConfigurationAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return pool.DisposeAsync();
        }
    }
}
