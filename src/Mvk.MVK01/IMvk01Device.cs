using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;

namespace Mvk.MVK01
{
    /// <summary>
    /// Типизированный фасад устройства МВК01: декодированные значения каналов,
    /// тахометров, сведений об устройстве и системные команды.
    /// </summary>
    public interface IMvk01Device
    {
        /// <summary>
        /// Идентификатор устройства из конфигурации.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Параметры устройства (с применёнными настройками по умолчанию).
        /// </summary>
        MvkDeviceSettings Settings { get; }

        /// <summary>
        /// Текущее состояние соединения.
        /// </summary>
        MvkDeviceState State { get; }

        /// <summary>
        /// Базовое устройство пула (сырые регистры, события состояния).
        /// </summary>
        IMvkDevice Device { get; }

        /// <summary>
        /// Устанавливает соединение явно.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task EnsureConnectedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Закрывает соединение.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает общие сведения об устройстве: серийный номер, версию ПО, ошибки, наработку.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01DeviceInfo> ReadDeviceInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает полный снимок канала АЦП: напряжение, спектр (43 полосы), СКЗ,
        /// пик-фактор, эквивалентные значения и состояния.
        /// </summary>
        /// <param name="channel">Номер канала (1–8).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01Channel> ReadChannelAsync(int channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает конфигурацию измерительного канала.
        /// </summary>
        /// <param name="channel">Номер канала (1–8).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01ChannelConfig> ReadChannelConfigAsync(int channel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает снимок канала тахометра.
        /// </summary>
        /// <param name="tacho">Номер тахометра (1–2).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01TachoMeasurement> ReadTachoAsync(int tacho, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает значения параметра по номеру для всех 8 каналов одним запросом
        /// (раздел 5.1.2, только ПО 1.0.61+).
        /// </summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<float[]> ReadMeasurementByNumberAsync(Mvk01MeasurementNumber number, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает значения параметра по числовому номеру для всех 8 каналов одним запросом.
        /// </summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<float[]> ReadMeasurementByNumberAsync(int number, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает регистры параметра по номеру без декодирования (для битовых полей).
        /// </summary>
        /// <param name="number">Номер параметра (1–184).</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<ushort[]> ReadMeasurementRawByNumberAsync(int number, CancellationToken cancellationToken = default);

        /// <summary>
        /// Выполняет системную команду с ожиданием статуса.
        /// </summary>
        /// <param name="command">Команда (Save/Reboot/Restore).</param>
        /// <param name="timeout">Общий таймаут операции; по умолчанию 3 с.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01CommandResult> ExecuteSystemCommandAsync(
            Mvk01SystemCommand command,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохраняет текущую конфигурацию во Flash (таймаут ответа до 3 с).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01CommandResult> SaveConfigurationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Восстанавливает конфигурацию из Flash.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01CommandResult> RestoreConfigurationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Перезапускает устройство.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<Mvk01CommandResult> RebootAsync(CancellationToken cancellationToken = default);
    }
}
