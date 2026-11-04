using System;

namespace Mvk.MVK01.Setpoints
{
    /// <summary>
    /// Настройки высокоуровневого сервиса уставок.
    /// </summary>
    public sealed class Mvk01AlarmOptions
    {
        /// <summary>
        /// Интервал опроса устройств. По умолчанию 500 мс.
        /// </summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Путь к JSON-файлу уставок для DI-регистрации.
        /// По умолчанию <c>mvk-setpoints.json</c>.
        /// </summary>
        public string SetpointsFile { get; set; } = "mvk-setpoints.json";

        /// <summary>
        /// Необязательный обработчик ошибок callback'а в
        /// <see cref="Mvk01AlarmService.RunAsync"/>. Вызывается после логирования,
        /// не останавливает наблюдение.
        /// </summary>
        public Action<Exception, MvkAlarmEvent>? OnReactionError { get; set; }
    }
}
