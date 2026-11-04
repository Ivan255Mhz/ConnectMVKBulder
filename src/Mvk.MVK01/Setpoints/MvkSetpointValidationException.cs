using System;
using System.Collections.Generic;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Некорректный набор уставок. Содержит список всех найденных ошибок.
    /// </summary>
    public sealed class MvkSetpointValidationException : Exception
    {
        /// <summary>
        /// Создаёт исключение с описанием ошибок.
        /// </summary>
        /// <param name="failures">Список ошибок валидации.</param>
        public MvkSetpointValidationException(IReadOnlyList<string> failures)
            : base("Некорректные уставки: " + string.Join(" ", failures))
        {
            Failures = failures ?? throw new ArgumentNullException(nameof(failures));
        }

        /// <summary>
        /// Найденные ошибки.
        /// </summary>
        public IReadOnlyList<string> Failures { get; }
    }
}
