using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Хранилище прикладных уставок. Реализация может быть файловой, SQLite,
    /// PostgreSQL или любой другой.
    /// </summary>
    public interface IMvkSetpointStore
    {
        /// <summary>
        /// Загружает все уставки.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        ValueTask<IReadOnlyList<MvkSetpoint>> LoadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Сохраняет набор уставок.
        /// </summary>
        /// <param name="setpoints">Уставки.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        ValueTask SaveAsync(
            IReadOnlyCollection<MvkSetpoint> setpoints,
            CancellationToken cancellationToken = default);
    }
}
