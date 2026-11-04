using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Высокоуровневый сервис уставок: опрашивает устройства, оценивает пороги
    /// с гистерезисом и отдаёт события переходов.
    /// </summary>
    public interface IMvk01AlarmService : IAsyncDisposable, IDisposable
    {
        /// <summary>
        /// Текущие состояния уставок. До запуска наблюдения список пуст.
        /// </summary>
        IReadOnlyList<MvkSetpointStatus> Statuses { get; }

        /// <summary>
        /// Поток событий тревог. Первый вызов запускает опрос; одновременно
        /// допускается только одно активное наблюдение.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        IAsyncEnumerable<MvkAlarmEvent> WatchAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Удобная обёртка над <see cref="WatchAsync"/>: вызывает обработчик
        /// для каждого события, изолируя ошибки обработчика.
        /// </summary>
        /// <param name="onAlarm">Обработчик события.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task RunAsync(
            Func<MvkAlarmEvent, CancellationToken, ValueTask> onAlarm,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Перечитывает уставки из хранилища. Разрешено только при остановленном
        /// наблюдении.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task ReloadSetpointsAsync(CancellationToken cancellationToken = default);
    }
}
