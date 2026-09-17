using System;
using System.Collections.Generic;
using System.Threading;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Контекст обработки одного запроса к устройству (аналог <c>HttpContext</c> в ASP.NET).
    /// </summary>
    public sealed class ModbusRequestContext
    {
        /// <summary>
        /// Создаёт контекст запроса.
        /// </summary>
        /// <param name="settings">Параметры устройства.</param>
        /// <param name="request">Запрос к устройству.</param>
        /// <param name="aborted">Токен отмены вызывающего кода.</param>
        public ModbusRequestContext(MvkDeviceSettings settings, ModbusRequest request, CancellationToken aborted)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Aborted = aborted;
        }

        /// <summary>
        /// Параметры устройства.
        /// </summary>
        public MvkDeviceSettings Settings { get; }

        /// <summary>
        /// Запрос к устройству.
        /// </summary>
        public ModbusRequest Request { get; }

        /// <summary>
        /// Токен отмены вызывающего кода.
        /// </summary>
        public CancellationToken Aborted { get; }

        /// <summary>
        /// Ответ пайплайна. Заполняется терминальным обработчиком или middleware
        /// при коротком замыкании.
        /// </summary>
        public ModbusResponse? Response { get; set; }

        /// <summary>
        /// Номер текущей попытки (устанавливает middleware ретраев).
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// Произвольные данные, которыми middleware обмениваются в рамках запроса.
        /// </summary>
        public IDictionary<string, object?> Items { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        /// <summary>
        /// Ответ получен (успешный или с ошибкой).
        /// </summary>
        public bool IsCompleted => Response is not null;

        /// <summary>
        /// Пробует получить значение из <see cref="Items"/>.
        /// </summary>
        /// <typeparam name="T">Тип значения.</typeparam>
        /// <param name="key">Ключ (см. <see cref="MvkContextItems"/>).</param>
        /// <param name="value">Найденное значение.</param>
        public bool TryGetItem<T>(string key, out T value)
        {
            if (Items.TryGetValue(key, out object? raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default!;
            return false;
        }
    }
}
