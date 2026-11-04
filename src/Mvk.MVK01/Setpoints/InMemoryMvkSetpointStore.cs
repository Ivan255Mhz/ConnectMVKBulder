using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Хранилище уставок в памяти. Удобно для тестов и программной конфигурации.
    /// </summary>
    public sealed class InMemoryMvkSetpointStore : IMvkSetpointStore
    {
        private readonly object sync = new();
        private MvkSetpoint[] setpoints;

        /// <summary>
        /// Создаёт хранилище с необязательным начальным набором.
        /// </summary>
        /// <param name="setpoints">Начальные уставки.</param>
        public InMemoryMvkSetpointStore(IEnumerable<MvkSetpoint>? setpoints = null)
        {
            this.setpoints = setpoints?.ToArray() ?? [];
            MvkSetpointValidator.ThrowIfInvalid(this.setpoints);
        }

        /// <inheritdoc/>
        public ValueTask<IReadOnlyList<MvkSetpoint>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (sync)
            {
                return ValueTask.FromResult<IReadOnlyList<MvkSetpoint>>([.. setpoints]);
            }
        }

        /// <inheritdoc/>
        public ValueTask SaveAsync(
            IReadOnlyCollection<MvkSetpoint> setpoints,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(setpoints);
            cancellationToken.ThrowIfCancellationRequested();
            MvkSetpointValidator.ThrowIfInvalid(setpoints);

            lock (sync)
            {
                this.setpoints = [.. setpoints];
            }

            return ValueTask.CompletedTask;
        }
    }
}
